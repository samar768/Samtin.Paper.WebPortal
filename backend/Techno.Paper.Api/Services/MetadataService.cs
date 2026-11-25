using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

public sealed record ColumnSpec(string Section, string Name, string SqlType, int Length, int Precision,
    int Scale, bool Nullable, bool Protected, string? DefaultDefinition);

public interface IMetadataService
{
    Task<SchemaResponse> GetAsync(SessionResponse session, CancellationToken ct);
    Task<IReadOnlyList<ColumnSpec>> DiscoverAsync(CancellationToken ct);
    Task ActivateAsync(SessionResponse session, ActivateFieldRequest request, CancellationToken ct);
}

public sealed class MetadataService(Database database) : IMetadataService
{
    private static readonly string[] HeaderKeys = ["RecId", "V_Date", "PartyCode", "PartyOrdNo", "PartyOrdDt",
        "Consignee", "DelAt", "DelDate", "FormCode", "Exciseable", "OrderType", "Remark"];
    private static readonly string[] LineKeys = ["ItemCode", "ItemDesc", "BF", "GSM", "SizeReel", "SizeHeight",
        "SizeUnit", "Grain", "QtySDU", "Qty", "Reel", "Rate", "Tl_ExcessQty", "TL_ShortageQty"];
    private static readonly HashSet<string> OriginalColumns = new(
        ("DocId V_Type V_No RecId V_Date V_Prefix AmdOrdDocId Ref_Con_DociD OrderType PartyOrdNo PartyOrdDt " +
        "PartyCode Consignee DelAt DelDate FormCode Exciseable GAmount TotalAmt ROffAmt NAmount Remark " +
        "Site_Code PreparedBy ModifiedBy ApprovedBy BookedBy U_EntDt U_AE Transfered V_TypeForSaleInvoice " +
        "CMN_Addition CMN_Deduction PL_DociD TOPlace Closed CGSTAmt CGSTPer SGSTAmt SGSTPer IGSTAmt IGSTPer " +
        "UTGSTAmt UTGSTPer CESSAmt CESSPer distributor V_SNo ItemCode ItemDesc BF SizeReel SizeHeight SizeUnit " +
        "SizeCM SizeInch GSM Grain QtySDU Qty Reel Rate Amount Tl_ExcessQty TL_ShortageQty AdjReels AdjReelQty " +
        "CloseForDeck Deck_reels DeckID AgreedRate ClosedType").Split(' '), StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ColumnSpec>> DiscoverAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(false, ct);
        var rows = await Database.QueryAsync(connection, null,
            """
            SELECT CASE t.name WHEN 'SOrd' THEN 'header' ELSE 'lines' END AS Section,
              c.name AS ColumnName,ty.name AS SqlType,c.max_length AS Length,c.precision AS Precision,c.scale AS Scale,
              c.is_nullable AS Nullable,dc.definition AS DefaultDefinition,
              CASE WHEN c.is_identity=1 OR c.is_computed=1 OR c.generated_always_type<>0 OR EXISTS(
               SELECT 1 FROM sys.index_columns i JOIN sys.indexes x ON x.object_id=i.object_id AND x.index_id=i.index_id
               WHERE i.object_id=c.object_id AND i.column_id=c.column_id AND x.is_primary_key=1)
               OR EXISTS(SELECT 1 FROM sys.foreign_key_columns f WHERE f.parent_object_id=c.object_id AND f.parent_column_id=c.column_id)
               THEN 1 ELSE 0 END AS Protected
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
            JOIN sys.types ty ON ty.user_type_id=c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
            WHERE s.name='dbo' AND t.name IN ('SOrd','SOrdDet') ORDER BY t.name,c.column_id
            """, ct);
        return rows.Select(r => new ColumnSpec(r["Section"]!, r["ColumnName"]!, r["SqlType"]!,
            int.Parse(r["Length"]!, CultureInfo.InvariantCulture), int.Parse(r["Precision"]!, CultureInfo.InvariantCulture),
            int.Parse(r["Scale"]!, CultureInfo.InvariantCulture), r["Nullable"] == "True", r["Protected"] == "1", r["DefaultDefinition"])).ToArray();
    }

    public async Task<SchemaResponse> GetAsync(SessionResponse session, CancellationToken ct)
    {
        if (!session.Capabilities.View) throw new ApiFault(403, "sales_denied", "Sales access is not permitted.");
        var columns = await DiscoverAsync(ct);
        await using var connection = await database.OpenAsync(false, ct);
        var activated = await Database.QueryAsync(connection, null,
            "SELECT Section,ColumnName,Label,Permission FROM webpoc.Field ORDER BY Section,ColumnName", ct);
        var lookups = await Database.QueryAsync(connection, null,
            """
            SELECT f.Section,f.FieldKey,f.LookupId FROM webpoc.LookupField f
            JOIN webpoc.LookupDefinition d ON d.Id=f.LookupId AND d.Resource=f.Resource AND d.Enabled=1
            WHERE f.Resource='sales-orders' ORDER BY f.Section,f.FieldKey
            """, ct);
        if (activated.Any(field => !columns.Any(column =>
            column.Section == field["Section"] && column.Name == field["ColumnName"])))
            throw new ApiFault(409, "active_field_missing", "An approved extension column is missing. Request an administrator schema review.");
        var fields = new List<FieldResponse>();
        foreach (var column in columns)
        {
            var core = (column.Section == "header" ? HeaderKeys : LineKeys).Contains(column.Name, StringComparer.OrdinalIgnoreCase);
            var extension = activated.SingleOrDefault(r => r["Section"] == column.Section && r["ColumnName"] == column.Name);
            if (!core && extension is null) continue;
            if (!core && !Eligible(column))
                throw new ApiFault(409, "active_field_unsupported", "An approved extension column has an unsupported definition. Request an administrator schema review.");
            if (!core && extension!["Permission"] == "M" && !session.Capabilities.MetadataAdministration)
                continue;
            var required = column.Section == "header"
                ? new[] { "V_Date", "PartyCode", "PartyOrdNo", "DelAt", "DelDate", "FormCode", "Exciseable", "OrderType" }.Contains(column.Name)
                : new[] { "ItemCode", "BF", "GSM", "SizeUnit", "Qty", "Rate" }.Contains(column.Name);
            var lookup = column.Name switch
            {
                "PartyCode" or "Consignee" or "DelAt" => "parties",
                "ItemCode" => "items", "BF" => "bf", "GSM" => "gsm", "FormCode" => "forms",
                "OrderType" => "orderTypes", "Exciseable" => "yesNo", "SizeUnit" => "sizeUnits", "Grain" => "grain",
                _ => null
            };
            fields.Add(new FieldResponse(column.Name, column.Section, extension?["Label"] ?? column.Name,
                column.SqlType, required, column.SqlType.StartsWith('n') ? column.Length / 2 : column.Length,
                column.Precision, column.Scale, lookup, true,
                lookups.SingleOrDefault(binding => binding["Section"] == column.Section &&
                    string.Equals(binding["FieldKey"], column.Name, StringComparison.OrdinalIgnoreCase))?.GetValueOrDefault("LookupId")));
        }
        var signature = JsonSerializer.Serialize(new { fields, activated, columns });
        return new SchemaResponse(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))), fields);
    }

    private static bool Eligible(ColumnSpec column) =>
        !column.Protected && column.Nullable && !OriginalColumns.Contains(column.Name) &&
        !new[] { "password", "secret", "token", "audit", "approved", "prepared", "modified", "permission" }
            .Any(s => column.Name.Contains(s, StringComparison.OrdinalIgnoreCase)) &&
        (column.SqlType is "int" or "smallint" or "bigint" or "tinyint" or "bit" or "date" or "datetime" or "datetime2" ||
         column.SqlType is "varchar" or "nvarchar" or "char" or "nchar" && column.Length is > 0 and <= 4000 ||
         column.SqlType is "decimal" or "numeric" && column.Precision <= 28 && column.Scale <= 8);

    public async Task ActivateAsync(SessionResponse session, ActivateFieldRequest request, CancellationToken ct)
    {
        if (!session.Capabilities.MetadataAdministration)
            throw new ApiFault(403, "metadata_denied", "Metadata administration is not permitted.");
        var column = (await DiscoverAsync(ct)).SingleOrDefault(c => c.Section == request.Section && c.Name == request.Column);
        if (column is null || !Eligible(column) || string.IsNullOrWhiteSpace(request.Label) ||
            request.Label.Length > 100 || request.Permission is not ("A" or "M"))
            throw new ApiFault(422, "unsupported_field", "Only eligible new nullable scalar columns can be activated. Permission must be A (authorized Sales users) or M (metadata administrator).");
        _ = LiteralDefault(column.DefaultDefinition, column.SqlType);
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await Database.ExecuteAsync(connection, transaction,
            """
            UPDATE webpoc.Field WITH(UPDLOCK,SERIALIZABLE) SET Label=@label,Permission=@permission
              WHERE Section=@section AND ColumnName=@column;
            IF @@ROWCOUNT=0 INSERT webpoc.Field(Section,ColumnName,Label,Permission) VALUES(@section,@column,@label,@permission);
            INSERT webpoc.Audit(Username,Operation,RecordId) VALUES(@user,'metadata.activate',@column);
            """, ct, ("@section", request.Section), ("@column", request.Column), ("@label", request.Label),
            ("@permission", request.Permission), ("@user", session.Username));
        await transaction.CommitAsync(ct);
    }

    public static object? Validate(FieldResponse field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (field.Required) throw new ApiFault(422, "required_field", $"{field.Section}.{field.Key} is required.");
            return null;
        }
        var valid = field.Type switch
        {
            "varchar" or "char" or "nvarchar" or "nchar" => field.Length < 0 || value.Length <= field.Length,
            "float" or "real" => decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out _),
            "decimal" or "numeric" => SalesRules.ValidDecimal(value, field.Precision, field.Scale),
            "int" => int.TryParse(value, out _),
            "smallint" => short.TryParse(value, out _),
            "tinyint" => byte.TryParse(value, out _),
            "bigint" => long.TryParse(value, out _),
            "bit" => value is "True" or "False" or "true" or "false",
            "date" or "smalldatetime" or "datetime" or "datetime2" => DateTime.TryParseExact(value,
                ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => false
        };
        if (!valid) throw new ApiFault(422, "invalid_field", $"{field.Section}.{field.Key} has an invalid value, length or precision.");
        if (field.Type is "varchar" or "char")
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try { _ = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetByteCount(value); }
            catch (EncoderFallbackException) { throw new ApiFault(422, "invalid_encoding", $"{field.Key} contains characters not representable in the legacy text column."); }
        }
        return field.Type switch
        {
            "float" or "real" or "decimal" or "numeric" => decimal.Parse(value, CultureInfo.InvariantCulture),
            "int" or "smallint" or "tinyint" => int.Parse(value, CultureInfo.InvariantCulture),
            "bigint" => long.Parse(value, CultureInfo.InvariantCulture),
            "bit" => bool.Parse(value),
            "date" or "smalldatetime" or "datetime" or "datetime2" => DateTime.Parse(value, CultureInfo.InvariantCulture),
            _ => value
        };
    }

    public static string? LiteralDefault(string? definition, string type)
    {
        if (definition is null) return null;
        var value = definition.Trim();
        while (value.StartsWith('(') && value.EndsWith(')')) value = value[1..^1].Trim();
        if (value.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return null;
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^[Nn]?'([^']|'')*'$"))
            return value[(value[0] is 'N' or 'n' ? 2 : 1)..^1].Replace("''", "'");
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^[+-]?\d+(\.\d+)?$"))
            return type == "bit" ? value == "1" ? "true" : value == "0" ? "false" :
                throw new ApiFault(422, "unsupported_default", "The configured Boolean default is invalid.") : value;
        throw new ApiFault(422, "unsupported_default", "Only literal database defaults are supported. SQL expressions require explicit review.");
    }

    public static Dictionary<string, string?> DefaultValues(SchemaResponse schema, IReadOnlyList<ColumnSpec> columns, string section) =>
        schema.Fields.Where(f => f.Section == section).ToDictionary(f => f.Key, field =>
        {
            var value = LiteralDefault(columns.Single(c => c.Section == section && c.Name == field.Key).DefaultDefinition, field.Type);
            if (value is not null) _ = Validate(field, value);
            return value;
        });
}
