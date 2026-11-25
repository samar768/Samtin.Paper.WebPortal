using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

public interface IMasterService
{
    Task<MasterFormResponse> FormAsync(SessionResponse session, string id, string? recordId, CancellationToken ct);
    Task<MasterListResponse> ListAsync(SessionResponse session, string id, string? search, int page, CancellationToken ct);
    Task<MasterRecordResponse> SaveAsync(SessionResponse session, string id, string? recordId, SaveMasterRequest request, CancellationToken ct);
    Task DeleteAsync(SessionResponse session, string id, string recordId, DeleteOrderRequest request, CancellationToken ct);
    Task<MasterPrintResponse> PrintAsync(SessionResponse session, string id, string? search, CancellationToken ct);
    Task<LookupCreateResponse?> LookupCreateAsync(SessionResponse session, string lookupId, CancellationToken ct);
    Task<MasterAuditResponse> AuditAsync(SessionResponse session, string id, string? recordId, int page, CancellationToken ct);
}

public sealed class MasterService(Database database, PocOptions options) : IMasterService
{
    private sealed record Source(MasterDefinitionResponse Definition, string Table, string Key, string Name, int KeyLength, string Site);
    private static ApiFault ConfigurationError() => new(409, "master_configuration",
        "This master configuration requires administrator review. No master has been changed.");
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string Identifier(string name)
    {
        if (name.StartsWith("__", StringComparison.Ordinal) || !Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
            throw ConfigurationError();
        return $"[{name}]";
    }
    private static void Require(bool allowed)
    {
        if (!allowed) throw new ApiFault(403, "master_denied", "This master operation is not permitted in the current workspace.");
    }

    private async Task<Dictionary<string, string?>> RegistrationAsync(SqlConnection connection, SqlTransaction? transaction,
        string id, CancellationToken ct)
    {
        if (!Regex.IsMatch(id, @"^[a-z0-9_-]{1,60}$"))
            throw new ApiFault(400, "invalid_master", "Invalid master identifier.");
        var rows = await Database.QueryAsync(connection, transaction,
            """
            SELECT d.*,r.CaptureAuditValues FROM webpoc.MasterRegistration r JOIN webpoc.MasterDefinition d
              ON d.MasterId=r.Id AND d.Revision=r.ActiveRevision WHERE r.Id=@id AND r.Enabled=1
            """, ct, ("@id", id));
        if (rows.Count != 1) throw new ApiFault(404, "master_not_found", "This master is not available.");
        return rows[0];
    }

    private async Task<MasterCapabilitiesResponse> CapabilitiesAsync(SessionResponse session,
        Dictionary<string, string?> definition, CancellationToken ct)
    {
        return await MasterAccess.ReadAsync(database, options, session, definition["MenuModule"]!, definition["MenuName"]!, ct);
    }

    private async Task<Source> LoadAsync(SqlConnection connection, SqlTransaction? transaction, SessionResponse session,
        string id, CancellationToken ct)
    {
        var definition = await RegistrationAsync(connection, transaction, id, ct);
        var capabilities = await CapabilitiesAsync(session, definition, ct);
        Require(capabilities.View);
        if (definition["Handler"] != "single-field") throw ConfigurationError();
        var table = definition["TableName"]!;
        var key = definition["KeyColumn"]!;
        var name = definition["NameColumn"]!;
        var scope = definition["Scope"];
        if (scope is not ("global" or "site")) throw ConfigurationError();
        _ = Identifier(table); _ = Identifier(key); _ = Identifier(name);
        var configured = await Database.QueryAsync(connection, transaction,
            "SELECT * FROM webpoc.MasterField WHERE MasterId=@id AND Revision=@revision ORDER BY Position",
            ct, ("@id", id), ("@revision", int.Parse(definition["Revision"]!, CultureInfo.InvariantCulture)));
        var columns = await Database.QueryAsync(connection, transaction,
            """
            SELECT c.name AS ColumnName,t.name AS SqlType,c.max_length AS Length,c.precision AS Precision,
              c.scale AS Scale,c.is_nullable AS Nullable,c.is_identity AS IdentityColumn,c.is_computed AS Computed,
              c.generated_always_type AS Generated,c.collation_name AS Collation,dc.definition AS DefaultDefinition,
              CASE WHEN EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i
                ON i.object_id=ic.object_id AND i.index_id=ic.index_id WHERE ic.object_id=c.object_id
                AND ic.column_id=c.column_id AND i.is_primary_key=1) OR EXISTS(
                SELECT 1 FROM sys.foreign_key_columns f WHERE f.parent_object_id=c.object_id
                AND f.parent_column_id=c.column_id) THEN 1 ELSE 0 END AS Protected
            FROM sys.tables m JOIN sys.schemas s ON s.schema_id=m.schema_id
            JOIN sys.columns c ON c.object_id=m.object_id JOIN sys.types t ON t.user_type_id=c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
            WHERE s.name='dbo' AND m.name=@table ORDER BY c.column_id
            """, ct, ("@table", table));
        var indexes = await Database.QueryAsync(connection, transaction,
            """
            SELECT i.name AS IndexName,i.is_primary_key AS PrimaryKey,c.name AS ColumnName,i.filter_definition AS Filter
            FROM sys.tables m JOIN sys.schemas s ON s.schema_id=m.schema_id JOIN sys.indexes i ON i.object_id=m.object_id
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE s.name='dbo' AND m.name=@table AND i.is_unique=1 AND i.is_disabled=0 AND ic.key_ordinal>0
            ORDER BY i.name,ic.key_ordinal
            """, ct, ("@table", table));
        bool Unique(string column, bool primary) => indexes.GroupBy(index => index["IndexName"]).Any(group =>
            group.Count() == 1 && group.First()["ColumnName"] == column &&
            (!primary || group.First()["PrimaryKey"] == "True") &&
            (group.First()["Filter"] is null || !primary && Regex.Replace(group.First()["Filter"]!, @"[\s()]", "")
                .Equals($"[{column}]ISNOTNULL", StringComparison.OrdinalIgnoreCase)));
        bool ScopedUnique() => indexes.GroupBy(index => index["IndexName"]).Any(group =>
            group.Count() == 2 && group.Any(index => index["ColumnName"] == "Site_Code") &&
            group.Any(index => index["ColumnName"] == name) &&
            (group.First()["Filter"] is null || Regex.Replace(group.First()["Filter"]!, @"[\s()]", "")
                .Equals($"[{name}]ISNOTNULLAND[Site_Code]ISNOTNULL", StringComparison.OrdinalIgnoreCase)));
        var keyColumn = columns.SingleOrDefault(column => column["ColumnName"] == key);
        if (keyColumn is null || keyColumn["SqlType"] != "varchar" || keyColumn["Length"] is not ("6" or "10") ||
            keyColumn["Protected"] != "1" || keyColumn["Nullable"] != "False" || !Unique(key, true) ||
            !(scope == "site" ? ScopedUnique() : Unique(name, false)) ||
            scope == "site" && !columns.Any(column => column["ColumnName"] == "Site_Code" && column["SqlType"] == "varchar" && column["Length"] == "2") ||
            !columns.Any(column => column["ColumnName"] == "WebPocVersion" && column["SqlType"] == "timestamp") ||
            !new[] { "U_Name", "U_EntDt", "U_AE", "Transfered" }.All(field => columns.Any(column => column["ColumnName"] == field)) ||
            configured.Count is < 1 or > 20 || !configured.Any(field => field["ColumnName"] == name && field["Required"] == "True"))
            throw ConfigurationError();
        var fields = new List<FieldResponse>();
        var defaults = new Dictionary<string, string?>();
        foreach (var field in configured)
        {
            var column = columns.SingleOrDefault(column => column["ColumnName"] == field["ColumnName"]);
            if (column is null) throw ConfigurationError();
            var fieldName = column["ColumnName"]!;
            _ = Identifier(fieldName);
            var type = column["SqlType"]!;
            var length = int.Parse(column["Length"]!, CultureInfo.InvariantCulture);
            if (column["Protected"] == "1" || column["IdentityColumn"] == "True" || column["Computed"] == "True" ||
                column["Generated"] != "0" || fieldName.StartsWith("U_", StringComparison.OrdinalIgnoreCase) ||
                new[] { "Transfered", "WebPocVersion", "Site_Code", "SiteList" }.Contains(fieldName, StringComparer.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(field["Label"]) ||
                new[] { "password", "secret", "token", "permission", "approved", "prepared", "modified", "audit" }
                    .Any(word => fieldName.Contains(word, StringComparison.OrdinalIgnoreCase)) ||
                fieldName != name && column["Nullable"] != "True" ||
                !(type is "int" or "smallint" or "bigint" or "tinyint" or "bit" or "date" or "datetime" or "datetime2" ||
                  type is "decimal" or "numeric" && int.Parse(column["Precision"]!, CultureInfo.InvariantCulture) <= 28 &&
                    int.Parse(column["Scale"]!, CultureInfo.InvariantCulture) <= 8 ||
                  type is "varchar" or "nvarchar" or "char" or "nchar" && length is > 0 and <= 4000))
                throw ConfigurationError();
            var inputLength = type.StartsWith('n') ? length / 2 : length;
            if (field["MaxLength"] is not null)
            {
                var configuredLength = int.Parse(field["MaxLength"]!, CultureInfo.InvariantCulture);
                if (type is not ("varchar" or "nvarchar" or "char" or "nchar") || configuredLength < 1 || configuredLength > inputLength)
                    throw ConfigurationError();
                inputLength = configuredLength;
            }
            var response = new FieldResponse(fieldName, "master", field["Label"]!, type,
                field["Required"] == "True" || column["Nullable"] == "False",
                inputLength, int.Parse(column["Precision"]!, CultureInfo.InvariantCulture),
                int.Parse(column["Scale"]!, CultureInfo.InvariantCulture), null, true);
            var defaultValue = field["DefaultValue"] ?? MetadataService.LiteralDefault(column["DefaultDefinition"], type);
            if (!string.IsNullOrEmpty(defaultValue)) _ = MetadataService.Validate(response, defaultValue);
            fields.Add(response); defaults.Add(fieldName, defaultValue);
        }
        if (fields.Single(field => field.Key == name).Type is not ("varchar" or "nvarchar"))
            throw ConfigurationError();
        if (columns.Any(column => column["Nullable"] == "False" && column["DefaultDefinition"] is null &&
            column["IdentityColumn"] != "True" && column["Computed"] != "True" && column["SqlType"] != "timestamp" &&
            column["Generated"] == "0" && column["ColumnName"] != key && !(scope == "site" && column["ColumnName"] == "Site_Code") &&
            !fields.Any(field => field.Key == column["ColumnName"]))) throw ConfigurationError();
        var bindings = await Database.QueryAsync(connection, transaction,
            "SELECT LookupId FROM webpoc.LookupMaster WHERE MasterId=@id ORDER BY LookupId", ct, ("@id", id));
        var metadata = new MasterDefinitionResponse(id, definition["Title"]!,
            int.Parse(definition["Revision"]!, CultureInfo.InvariantCulture), Hash(new { definition, configured, columns, indexes }),
            fields, defaults, capabilities, bindings.Select(binding => binding["LookupId"]!).ToList(), scope!,
            definition["CaptureAuditValues"] == "True");
        return new(metadata, $"dbo.{Identifier(table)}", key, name,
            int.Parse(keyColumn["Length"]!, CultureInfo.InvariantCulture), session.Site);
    }

    private static byte[] Version(string? value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value ?? "");
            if (bytes.Length == 8) return bytes;
        }
        catch (FormatException) { }
        throw new ApiFault(422, "invalid_master_version", "A valid master concurrency token is required.");
    }

    private static string Projection(Source source) =>
        string.Join(",", source.Definition.Fields.Select(field => $"m.{Identifier(field.Key)}")) +
        $"""
        ,m.{Identifier(source.Key)} AS __Id,m.WebPocVersion AS __Version,m.U_Name AS __User,m.U_AE AS __Mode,
        (SELECT TOP (1) a.Username FROM webpoc.MasterAudit a WHERE a.MasterId=@master
         AND a.RecordId=m.{Identifier(source.Key)} AND a.Operation='create'
         AND a.Id>ISNULL((SELECT MAX(deleted.Id) FROM webpoc.MasterAudit deleted WHERE deleted.MasterId=@master
           AND deleted.RecordId=a.RecordId AND deleted.Operation='delete'),0) ORDER BY a.Id) AS __CreatedBy
        """;

    private static MasterRecordResponse Record(Source source, Dictionary<string, string?> row) => new(
        row["__Id"]!, row["__Version"]!, source.Definition.Version,
        source.Definition.Fields.ToDictionary(field => field.Key, field => row[field.Key]),
        new(row["__CreatedBy"] ?? (row["__Mode"] == "A" ? row["__User"] : null),
            row["__Mode"] == "E" ? row["__User"] : null, null, null));

    private static void CheckRecordId(Source source, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > source.KeyLength)
            throw new ApiFault(400, "invalid_master_key", "Invalid master record key.");
    }

    private static async Task<MasterRecordResponse> GetAsync(SqlConnection connection, SqlTransaction? transaction,
        Source source, string recordId, CancellationToken ct)
    {
        CheckRecordId(source, recordId);
        var rows = await Database.QueryAsync(connection, transaction,
            $"SELECT {Projection(source)} FROM {source.Table} m WHERE m.{Identifier(source.Key)}=@key {SiteFilter(source, "m")}",
            ct, ("@key", recordId), ("@master", source.Definition.Id), ("@site", source.Site));
        if (rows.Count != 1) throw new ApiFault(404, "master_record_not_found", "The master record no longer exists.");
        return Record(source, rows[0]);
    }

    public async Task<MasterFormResponse> FormAsync(SessionResponse session, string id, string? recordId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(false, ct);
        var source = await LoadAsync(connection, null, session, id, ct);
        if (recordId is null) Require(source.Definition.Capabilities.Add);
        return new(source.Definition, recordId is null ? null : await GetAsync(connection, null, source, recordId, ct));
    }

    public async Task<MasterListResponse> ListAsync(SessionResponse session, string id, string? search, int page, CancellationToken ct)
    {
        if (search?.Length > 100 || page is < 0 or > 10000)
            throw new ApiFault(400, "invalid_master_search", "Invalid master search or page.");
        await using var connection = await database.OpenAsync(false, ct);
        var source = await LoadAsync(connection, null, session, id, ct);
        var rows = await SearchAsync(connection, source, search, page * 25, 26, ct);
        return new(source.Definition, rows.Take(25).Select(row => Record(source, row)).ToList(), page, rows.Count > 25);
    }

    private static Task<List<Dictionary<string, string?>>> SearchAsync(SqlConnection connection, Source source,
        string? search, int skip, int take, CancellationToken ct) => Database.QueryAsync(connection, null,
        $"""
        WITH Records AS (SELECT {Projection(source)},ROW_NUMBER() OVER(ORDER BY m.{Identifier(source.Name)},
          m.{Identifier(source.Key)}) AS __Row FROM {source.Table} m
          WHERE (@search='' OR CHARINDEX(@search,m.{Identifier(source.Name)})>0 OR CHARINDEX(@search,m.{Identifier(source.Key)})>0)
          {SiteFilter(source, "m")})
        SELECT * FROM Records WHERE __Row>@skip AND __Row<=@skip+@take ORDER BY __Row
        """, ct, ("@master", source.Definition.Id), ("@search", search?.Trim() ?? ""), ("@skip", skip), ("@take", take), ("@site", source.Site));

    private static string SiteFilter(Source source, string? alias = null) =>
        source.Definition.Scope == "site" ? $"AND {(alias is null ? "" : alias + ".")}Site_Code=@site" : "";

    private static Task<int> WriteAuditAsync(SqlConnection connection, SqlTransaction transaction, Source source,
        SessionResponse session, string recordId, string operation, Dictionary<string, string?>? before,
        Dictionary<string, string?>? after, CancellationToken ct) =>
        MasterAuditLog.WriteAsync(connection, transaction, source.Definition, session, recordId, operation, before, after, ct);

    public async Task<MasterRecordResponse> SaveAsync(SessionResponse session, string id, string? recordId,
        SaveMasterRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.IdempotencyKey, out _) || request.Values.Count > 20)
            throw new ApiFault(422, "invalid_master_request", "A valid command identifier and bounded master values are required.");
        await using var connection = await database.OpenAsync(false, ct);
        await Database.ExecuteAsync(connection, null, "SET XACT_ABORT ON; SET LOCK_TIMEOUT 10000;", ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var source = await LoadAsync(connection, transaction, session, id, ct);
            Require(recordId is null ? source.Definition.Capabilities.Add : source.Definition.Capabilities.Edit);
            var commandKey = Hash(new { session.Username, session.Company, session.Site, Master = id, request.IdempotencyKey });
            var payloadHash = Hash(new { recordId, request });
            var command = await Database.QueryAsync(connection, transaction,
                "SELECT PayloadHash,ResponseJson FROM webpoc.MasterCommand WITH(UPDLOCK,HOLDLOCK) WHERE CommandKey=@key",
                ct, ("@key", commandKey));
            if (command.Count > 0)
            {
                if (command[0]["PayloadHash"] != payloadHash)
                    throw new ApiFault(409, "master_command_reused", "This command identifier was already used for different master values.");
                return JsonSerializer.Deserialize<MasterRecordResponse>(command[0]["ResponseJson"]!) ?? throw ConfigurationError();
            }
            if (request.MetadataVersion != source.Definition.Version)
                throw new ApiFault(409, "master_metadata_changed", "The master fields changed. Preserve your values and reopen the form before saving.");
            if (request.Values.Keys.Any(key => !source.Definition.Fields.Any(field => field.Key == key)))
                throw new ApiFault(422, "unknown_master_field", "The request contains an unapproved or server-owned master field.");
            if (recordId is null && request.Version is not null)
                throw new ApiFault(422, "invalid_master_version", "New masters must not carry an existing record version.");
            MasterRecordResponse? before = recordId is null ? null : await GetAsync(connection, transaction, source, recordId, ct);
            if (before is not null && before.Version != Convert.ToBase64String(Version(request.Version)))
                throw new ApiFault(409, "master_conflict", "This master changed in another session. Reopen it before editing.");
            var values = new Dictionary<string, string?>(before?.Values ?? source.Definition.Defaults);
            foreach (var (key, value) in request.Values) values[key] = value;
            values[source.Name] = Regex.Replace((values[source.Name] ?? "").Trim(), " {2,}", " ");
            var parameters = source.Definition.Fields.Select((field, index) =>
                ($"@value{index}", MetadataService.Validate(field, values[field.Key]))).ToList();
            if (recordId is null)
            {
                if (session.Site.Length != 2) throw ConfigurationError();
                var numbering = await Database.QueryAsync(connection, transaction,
                    $"""
                    SELECT {Identifier(source.Key)} AS Code FROM {source.Table} WITH(UPDLOCK,HOLDLOCK)
                    WHERE LEFT({Identifier(source.Key)},2)=@site
                    UNION SELECT RecordId FROM webpoc.MasterAudit WHERE MasterId=@master AND LEFT(RecordId,2)=@site
                    """, ct, ("@site", session.Site), ("@master", id));
                var numbers = numbering.Select(row => row["Code"]![2..]).ToList();
                if (numbers.Any(number => !int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                    throw new ApiFault(409, "master_numbering", "Existing master keys have an incompatible site suffix. Request administrator review.");
                var next = Math.Max(0, numbers.Select(number => int.Parse(number, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max()) + 1;
                if (next > (source.KeyLength == 10 ? 99999999 : 9999))
                    throw new ApiFault(409, "master_numbering", "The master key range is exhausted for this site.");
                recordId = session.Site + next.ToString(CultureInfo.InvariantCulture);
                parameters.Add(("@key", recordId)); parameters.Add(("@user", session.Username)); parameters.Add(("@site", session.Site));
                await Database.ExecuteAsync(connection, transaction,
                    $"""
                    INSERT INTO {source.Table}({Identifier(source.Key)},{string.Join(",", source.Definition.Fields.Select(field => Identifier(field.Key)))},U_Name,U_EntDt,U_AE{(source.Definition.Scope == "site" ? ",Site_Code" : "")})
                    VALUES(@key,{string.Join(",", source.Definition.Fields.Select((_, index) => $"@value{index}"))},@user,GETDATE(),'A'{(source.Definition.Scope == "site" ? ",@site" : "")})
                    """, ct, parameters.ToArray());
            }
            else
            {
                parameters.Add(("@key", recordId)); parameters.Add(("@user", session.Username));
                parameters.Add(("@version", Version(request.Version)));
                parameters.Add(("@site", session.Site));
                var changed = await Database.ExecuteAsync(connection, transaction,
                    $"""
                    UPDATE {source.Table} SET {string.Join(",", source.Definition.Fields.Select((field, index) => $"{Identifier(field.Key)}=@value{index}"))},
                      Transfered='N',U_Name=@user,U_EntDt=GETDATE(),U_AE='E'
                    WHERE {Identifier(source.Key)}=@key AND WebPocVersion=@version {SiteFilter(source)}
                    """, ct, parameters.ToArray());
                if (changed != 1) throw new ApiFault(409, "master_conflict", "This master changed in another session. Reopen it before editing.");
            }
            await WriteAuditAsync(connection, transaction, source, session, recordId, before is null ? "create" : "edit",
                before?.Values, values, ct);
            var saved = await GetAsync(connection, transaction, source, recordId, ct);
            await Database.ExecuteAsync(connection, transaction,
                "INSERT INTO webpoc.MasterCommand(CommandKey,MasterId,RecordId,PayloadHash,ResponseJson) VALUES(@command,@master,@key,@hash,@response)",
                ct, ("@command", commandKey), ("@master", id), ("@key", recordId), ("@hash", payloadHash),
                ("@response", JsonSerializer.Serialize(saved)));
            await transaction.CommitAsync(ct);
            return saved;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new ApiFault(409, "master_duplicate", "A master with that name or key already exists. No duplicate was saved.");
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222)
        {
            throw new ApiFault(409, "master_busy", "Another session is updating this master. Retry after it completes.");
        }
    }

    public async Task DeleteAsync(SessionResponse session, string id, string recordId, DeleteOrderRequest request, CancellationToken ct)
    {
        var version = Version(request.Version);
        await using var connection = await database.OpenAsync(false, ct);
        await Database.ExecuteAsync(connection, null, "SET XACT_ABORT ON; SET LOCK_TIMEOUT 10000;", ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var source = await LoadAsync(connection, transaction, session, id, ct);
            Require(source.Definition.Capabilities.Delete);
            var before = await GetAsync(connection, transaction, source, recordId, ct);
            if (before.Version != request.Version)
                throw new ApiFault(409, "master_conflict", "This master changed in another session. Reopen it before deleting.");
            var changed = await Database.ExecuteAsync(connection, transaction,
                $"DELETE FROM {source.Table} WHERE {Identifier(source.Key)}=@key AND WebPocVersion=@version {SiteFilter(source)}",
                ct, ("@key", recordId), ("@version", version), ("@site", session.Site));
            if (changed != 1) throw new ApiFault(409, "master_conflict", "This master changed in another session.");
            await WriteAuditAsync(connection, transaction, source, session, recordId, "delete", before.Values, null, ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            throw new ApiFault(409, "master_in_use", "This master is used by an ERP transaction or another master and cannot be deleted.");
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222)
        {
            throw new ApiFault(409, "master_busy", "Another session is updating this master. Retry after it completes.");
        }
    }

    public async Task<MasterPrintResponse> PrintAsync(SessionResponse session, string id, string? search, CancellationToken ct)
    {
        if (search?.Length > 100) throw new ApiFault(400, "invalid_master_search", "The print filter is too long.");
        await using var connection = await database.OpenAsync(false, ct);
        var source = await LoadAsync(connection, null, session, id, ct);
        Require(source.Definition.Capabilities.Print);
        var rows = await SearchAsync(connection, source, search, 0, 1001, ct);
        if (rows.Count > 1000) throw new ApiFault(422, "master_print_limit", "Filter the master list to at most 1000 records before printing.");
        await Database.ExecuteAsync(connection, null,
            "INSERT INTO webpoc.Audit(Username,Operation,RecordId) VALUES(@user,'master.print',@master)",
            ct, ("@user", session.Username), ("@master", id));
        return new(source.Definition, rows.Select(row => Record(source, row)).ToList());
    }

    public async Task<MasterAuditResponse> AuditAsync(SessionResponse session, string id, string? recordId, int page, CancellationToken ct)
    {
        if (page is < 0 or > 10000) throw new ApiFault(400, "invalid_audit_page", "Invalid audit page.");
        await using var connection = await database.OpenAsync(false, ct);
        var source = await LoadAsync(connection, null, session, id, ct);
        Require(source.Definition.Capabilities.Audit);
        if (recordId is not null) CheckRecordId(source, recordId);
        return await MasterAuditLog.ReadAsync(connection, source.Definition, session, recordId, page, ct);
    }

    public async Task<LookupCreateResponse?> LookupCreateAsync(SessionResponse session, string lookupId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(false, ct);
        var binding = await Database.QueryAsync(connection, null,
            """
            SELECT d.* FROM webpoc.LookupMaster b JOIN webpoc.MasterRegistration r ON r.Id=b.MasterId
              JOIN webpoc.MasterDefinition d ON d.MasterId=r.Id AND d.Revision=r.ActiveRevision
            WHERE b.LookupId=@lookup AND r.Enabled=1
            """, ct, ("@lookup", lookupId));
        if (binding.Count == 0) return null;
        var definition = binding[0];
        var capabilities = await CapabilitiesAsync(session, definition, ct);
        return capabilities.Add ? new(definition["MasterId"]!, definition["Title"]!) : null;
    }
}
