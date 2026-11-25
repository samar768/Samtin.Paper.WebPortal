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

public interface IOrderService
{
    Task<IReadOnlyList<Dictionary<string, string?>>> ListAsync(SessionResponse session, string? search, int page, CancellationToken ct,
        int pageSize = 25, string sort = "date", string direction = "desc");
    Task<OrderResponse> GetAsync(SessionResponse session, string id, CancellationToken ct);
    Task<DataSet> LoadPrintDataAsync(SessionResponse session, string id, CancellationToken ct);
    Task<OrderResponse> DefaultsAsync(SessionResponse session, CancellationToken ct, string? voucherType = null);
    Task<List<SalesOrderVoucher>> VoucherTypesAsync(SessionResponse session, CancellationToken ct);
    Task<OrderResponse> CopyAsync(SessionResponse session, string id, CancellationToken ct);
    Task<LineDefaultsResponse> LineDefaultsAsync(SessionResponse session, CancellationToken ct, string? voucherType = null);
    Task<List<ItemExpenseDefault>> ItemExpenseDefaultsAsync(SessionResponse session, string itemCode, CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, string?>>> LookupAsync(SessionResponse session, string key, string? search, CancellationToken ct);
    Task<OrderResponse> SaveAsync(SessionResponse session, string? id, SaveOrderRequest request, bool preview, CancellationToken ct);
    Task<DraftCalculationResponse> CalculateDraftAsync(SessionResponse session, DraftCalculationRequest request, CancellationToken ct);
    Task<ToleranceResponse> CalculateToleranceAsync(SessionResponse session, ToleranceRequest request, CancellationToken ct);
    Task DeleteAsync(SessionResponse session, string id, DeleteOrderRequest request, CancellationToken ct);
    Task<ApprovalStateResponse> ApprovalAsync(SessionResponse session, string id, CancellationToken ct);
    Task<OrderResponse> DecideAsync(SessionResponse session, string id, OrderDecisionRequest request, CancellationToken ct);
    Task<OrderResponse> UnapproveAsync(SessionResponse session, string id, UnapproveOrderRequest request, CancellationToken ct);
}

public sealed class OrderService(Database database, IMetadataService metadata, PocOptions options, IWriteCheckpoint checkpoint, ITaskService tasks) : IOrderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static string Hash(object value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions))));
    private static string? Wire(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
    private static DateTime DateBound(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);

    private static void Require(bool allowed)
    {
        if (!allowed) throw new ApiFault(403, "action_denied", "This Sales action is not permitted.");
    }

    private Task<string?> CutoffAsync(SessionResponse session, string action, CancellationToken ct) =>
        SalesDateAccess.CutoffAsync(database, session, action, ct);
    private static void CheckDate(SessionResponse session, string? date, string? cutoff) =>
        SalesDateAccess.CheckDate(session, date, cutoff);

    public async Task<IReadOnlyList<Dictionary<string, string?>>> ListAsync(SessionResponse session, string? search, int page, CancellationToken ct,
        int pageSize = 25, string sort = "date", string direction = "desc")
    {
        Require(session.Capabilities.View);
        if (page is < 0 or > 10000 || search?.Length > 100)
            throw new ApiFault(400, "invalid_search", "Invalid search or page.");
        ListingPolicy.ValidatePageSize(pageSize);
        var orderBy = ListingPolicy.OrderBy(sort, direction);
        var cutoff = await CutoffAsync(session, "view", ct);
        await using var connection = await database.OpenAsync(false, ct);
        return await Database.QueryAsync(connection, null,
            $"""
            WITH Orders AS (
              SELECT s.DocId,s.RecId,CONVERT(varchar(10),s.V_Date,23) AS V_Date,p.Name AS Party,
                s.GAmount,s.NAmount,s.ApprovedBy,ROW_NUMBER() OVER(ORDER BY {orderBy}) AS RowNumber
              FROM dbo.SOrd s LEFT JOIN dbo.SubGroup p ON p.SubCode=s.PartyCode
              WHERE s.Site_Code=@site AND s.V_Prefix=@prefix AND s.V_Type='SORD'
                AND s.V_Date>=@start AND s.V_Date<DATEADD(day,1,@end)
                AND (@cutoff IS NULL OR s.V_Date>=@cutoff)
                AND (@search='' OR CHARINDEX(@search,s.RecId)>0 OR CHARINDEX(@search,ISNULL(p.Name,''))>0))
            SELECT DocId,RecId,V_Date,Party,GAmount,NAmount,ApprovedBy
            FROM Orders WHERE RowNumber>@skip AND RowNumber<=@skip+@take ORDER BY RowNumber
            """, ct, ("@site", session.Site), ("@prefix", session.Prefix), ("@cutoff", cutoff is null ? null : DateBound(cutoff)),
            ("@search", search?.Trim() ?? ""), ("@skip", page * pageSize), ("@take", pageSize),
            ("@start", DateBound(session.StartDate)), ("@end", DateBound(session.EndDate)));
    }

    public async Task<IReadOnlyList<Dictionary<string, string?>>> LookupAsync(SessionResponse session, string key, string? search, CancellationToken ct)
    {
        Require(session.Capabilities.View);
        if (search?.Length > 100) throw new ApiFault(400, "invalid_lookup", "Lookup search is too long.");
        var predefined = key switch
        {
            "orderTypes" => new[] { ("R", "Paper"), ("S", "Sheet"), ("O", "Other") },
            "yesNo" => [("Y", "Yes"), ("N", "No")],
            "sizeUnits" => [("CM", "CM"), ("MM", "MM"), ("INCH", "Inch")],
            "grain" => [("L", "Long"), ("S", "Short")],
            _ => null
        };
        if (predefined is not null)
            return predefined.Select(p => new Dictionary<string, string?> { ["code"] = p.Item1, ["label"] = p.Item2 }).ToArray();
        var (table, code, name, siteFilter) = key switch
        {
            "parties" => ("SubGroup", "SubCode", "Name", true),
            "items" => ("ItemMast", "Code", "Name", true),
            "bf" => ("BFMast", "Code", "Name", false),
            "gsm" => ("GSMMast", "Code", "Name", false),
            "forms" => ("TaxForms", "Form_Code", "Form_Desc", false),
            _ => throw new ApiFault(404, "lookup_not_found", "Unknown lookup.")
        };
        await using var connection = await database.OpenAsync(false, ct);
        var filter = siteFilter ? "AND (NULLIF(SiteList,'') IS NULL OR CHARINDEX('|' + @site + '|','|' + SiteList + '|')>0)" : "";
        var itemTypeFilter = key == "items" ? "AND ItemType='FM'" : "";
        return await Database.QueryAsync(connection, null,
            $"""
            SELECT TOP(50) [{code}] AS code,[{name}] AS label FROM dbo.[{table}]
            WHERE (@search='' OR CHARINDEX(@search,[{name}])>0 OR CHARINDEX(@search,[{code}])>0) {filter} {itemTypeFilter}
            ORDER BY [{name}]
            """, ct, ("@search", search?.Trim() ?? ""), ("@site", session.Site));
    }

    private const string NewOrderVoucher = "SORD";

    public async Task<List<SalesOrderVoucher>> VoucherTypesAsync(SessionResponse session, CancellationToken ct)
    {
        Require(session.Capabilities.Add);
        await using var connection = await database.OpenAsync(false, ct);
        var rows = await Database.QueryAsync(connection, null,
            """
            SELECT V_Type, Description FROM dbo.Voucher_Type
            WHERE Category='SORD' AND ISNULL(ActiveYN,'Y')='Y'
            ORDER BY Description,V_Type
            """, ct);
        return rows.Select(row => new SalesOrderVoucher(row["V_Type"]!.Trim(),
            row["Description"]?.Trim() ?? row["V_Type"]!.Trim(), row["V_Type"]!.Trim() == NewOrderVoucher)).ToList();
    }

    private static async Task<string> ResolveVoucherAsync(SqlConnection connection, SqlTransaction? transaction,
        string? voucherType, CancellationToken ct)
    {
        if (voucherType is null) return NewOrderVoucher;
        var rows = await Database.QueryAsync(connection, transaction,
            """
            SELECT V_Type FROM dbo.Voucher_Type
            WHERE Category='SORD' AND ISNULL(ActiveYN,'Y')='Y' AND V_Type=@voucher
            """, ct, ("@voucher", voucherType));
        if (rows.Count != 1) throw new ApiFault(422, "invalid_voucher", "Select an active Sales Order voucher type.");
        return rows[0]["V_Type"]!.Trim();
    }

    public async Task<List<ItemExpenseDefault>> ItemExpenseDefaultsAsync(SessionResponse session, string itemCode, CancellationToken ct)
    {
        Require(session.Capabilities.View && (session.Capabilities.Add || session.Capabilities.Edit) && !session.ReadOnly);
        if (string.IsNullOrWhiteSpace(itemCode) || itemCode.Length > 100)
            throw new ApiFault(422, "invalid_lookup", "Select an item available in this workspace.");
        await using var connection = await database.OpenAsync(false, ct);
        return await VoucherExpenseStore.LoadItemDefaultsAsync(connection, null, itemCode, session.Site, ct);
    }

    private static Task<List<Dictionary<string, string?>>> DefinitionsAsync(SqlConnection connection,
        SqlTransaction? transaction, bool lines, CancellationToken ct, string voucher = NewOrderVoucher) =>
        VoucherExpenseStore.LoadTemplateAsync(connection, transaction, voucher, lines, ct);

    private Task<List<Dictionary<string, string?>>> TermDefinitionsAsync(SqlConnection connection,
        SqlTransaction? transaction, CancellationToken ct, string voucher = NewOrderVoucher,
        Dictionary<string, TermLookupSource>? bindings = null) =>
        VoucherTerms.LoadAsync(connection, transaction, voucher, ct, bindings, "sales-orders", "SORD");

    public async Task<OrderResponse> DefaultsAsync(SessionResponse session, CancellationToken ct, string? voucherType = null)
    {
        Require(session.Capabilities.Add);
        var schema = await metadata.GetAsync(session, ct);
        await using var connection = await database.OpenAsync(false, ct);
        var voucher = await ResolveVoucherAsync(connection, null, voucherType ?? NewOrderVoucher, ct);
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var date = string.CompareOrdinal(today, session.StartDate) < 0 ? session.StartDate :
            string.CompareOrdinal(today, session.EndDate) > 0 ? session.EndDate : today;
        var header = MetadataService.DefaultValues(schema, await metadata.DiscoverAsync(ct), "header");
        header["V_Date"] = date;
        header["DelDate"] = date;
        header["OrderType"] = "R";
        header["Exciseable"] = "Y";
        header["V_Type"] = voucher;
        var numbers = await DocumentNumbering.NextAsync(connection, null, "SOrd", session.Site, voucher, session.Prefix, ct);
        header["RecId"] = numbers.Reference;
        return new OrderResponse(null, null, schema.Version, header, [],
            await DefinitionsAsync(connection, null, false, ct, voucher), [], await TermDefinitionsAsync(connection, null, ct, voucher), false, null, numbers);
    }

    public async Task<LineDefaultsResponse> LineDefaultsAsync(SessionResponse session, CancellationToken ct, string? voucherType = null)
    {
        Require(session.Capabilities.Add || session.Capabilities.Edit);
        if (voucherType is not null) Require(session.Capabilities.Add);
        var schema = await metadata.GetAsync(session, ct);
        var values = MetadataService.DefaultValues(schema, await metadata.DiscoverAsync(ct), "lines");
        values["SizeUnit"] = "CM"; values["Grain"] = "L";
        foreach (var key in new[] { "Qty", "QtySDU", "Reel", "Rate", "Tl_ExcessQty", "TL_ShortageQty" }) values[key] = "0";
        await using var connection = await database.OpenAsync(false, ct);
        var voucher = await ResolveVoucherAsync(connection, null, voucherType, ct);
        return new LineDefaultsResponse(values, await DefinitionsAsync(connection, null, true, ct, voucher));
    }

    private sealed record StoredOrder(Dictionary<string, string?> Header, List<Dictionary<string, string?>> Lines,
        List<Dictionary<string, string?>> HeaderExpenses, List<Dictionary<string, string?>> LineExpenses,
        List<Dictionary<string, string?>> Terms, List<Dictionary<string, string?>> Allocations, bool Used)
    {
        public string Version => Hash(new { Header, Lines, HeaderExpenses = WithoutLabels(HeaderExpenses),
            LineExpenses = WithoutLabels(LineExpenses), Terms, Allocations });
        private static List<Dictionary<string, string?>> WithoutLabels(List<Dictionary<string, string?>> rows) =>
            rows.Select(row => row.Where(pair => pair.Key != "Label").ToDictionary()).ToList();
        public string? LockReason =>
            !string.IsNullOrWhiteSpace(Header.GetValueOrDefault("ApprovedBy")) ? "Approved orders cannot be edited or deleted." : ChangeLockReason;
        public string? ChangeLockReason =>
            Header.GetValueOrDefault("Closed") == "Y" || Lines.Any(l => l.GetValueOrDefault("Closed") == "Y") ? "Closed orders are read-only in this POC." :
            Used ? "This order has downstream use or ledger postings. The POC permits viewing but not changing it." :
            Allocations.Count > 0 ? "This order has reel allocations. Allocation editing is outside this POC." : null;
    }

    private static async Task<Dictionary<string, string?>> LoadHeaderAsync(SqlConnection connection, SqlTransaction? transaction,
        SessionResponse session, string id, CancellationToken ct)
    {
        var parameters = new (string Name, object? Value)[] { ("@id", id), ("@site", session.Site), ("@prefix", session.Prefix),
            ("@start", DateBound(session.StartDate)), ("@end", DateBound(session.EndDate)) };
        var headers = await Database.QueryAsync(connection, transaction,
            """
            SELECT * FROM dbo.SOrd WHERE DocId=@id AND Site_Code=@site AND V_Prefix=@prefix AND V_Type='SORD'
              AND V_Date>=@start AND V_Date<DATEADD(day,1,@end)
            """,
            ct, parameters);
        if (headers.Count != 1) throw new ApiFault(404, "order_not_found", "Order not found in the selected context.");
        return headers[0];
    }

    private static async Task<StoredOrder> LoadAsync(SqlConnection connection, SqlTransaction? transaction,
        SessionResponse session, string id, CancellationToken ct)
    {
        var header = await LoadHeaderAsync(connection, transaction, session, id, ct);
        var parameters = new (string Name, object? Value)[] { ("@id", id) };
        var lines = await Database.QueryAsync(connection, transaction, "SELECT * FROM dbo.SOrdDet WHERE DocId=@id ORDER BY V_SNo", ct, parameters);
        var expenses = await VoucherExpenseStore.LoadSnapshotAsync(connection, transaction, id, false, ct);
        var lineExpenses = await VoucherExpenseStore.LoadSnapshotAsync(connection, transaction, id, true, ct);
        var terms = await Database.QueryAsync(connection, transaction, "SELECT * FROM dbo.Term_Cond_Trans WHERE Ref_DocId=@id ORDER BY V_SNo,TCMast_Code", ct, parameters);
        var allocations = await Database.QueryAsync(connection, transaction, "SELECT * FROM dbo.Sord_adjustment WHERE SordDocID=@id ORDER BY Sord_adjustment_id", ct, parameters);
        var uses = await Database.QueryAsync(connection, transaction,
            """
            SELECT 1 AS Used WHERE EXISTS(SELECT 1 FROM dbo.WOrderDetail WHERE SODocId=@id)
              OR EXISTS(SELECT 1 FROM dbo.SaleDetail WHERE OrdDocID=@id)
              OR EXISTS(SELECT 1 FROM dbo.Ledger WHERE DocId=@id)
              OR EXISTS(SELECT 1 FROM dbo.SPExpenseMA WHERE DocId=@id)
            """, ct, parameters);
        return new StoredOrder(header, lines, expenses, lineExpenses, terms, allocations, uses.Count > 0);
    }

    private static Dictionary<string, string?> Project(Dictionary<string, string?> row, SchemaResponse schema, string section) =>
        schema.Fields.Where(f => f.Section == section).ToDictionary(f => f.Key, f => row.GetValueOrDefault(f.Key));

    public async Task<OrderResponse> GetAsync(SessionResponse session, string id, CancellationToken ct)
    {
        Require(session.Capabilities.View);
        var schema = await metadata.GetAsync(session, ct);
        var cutoff = await CutoffAsync(session, "view", ct);
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var stored = await LoadAsync(connection, transaction, session, id, ct);
        CheckDate(session, stored.Header["V_Date"], cutoff);
        var termDefinitions = await TermDefinitionsAsync(connection, transaction, ct, stored.Header["V_Type"]!.Trim());
        var lineExpenses = stored.Lines.Select(line => VoucherExpenseStore.ForLine(stored.LineExpenses, line["V_SNo"]!)).ToList();
        foreach (var row in termDefinitions)
        {
            var value = stored.Terms.SingleOrDefault(t => t["TCMast_Code"] == row["Code"]);
            if (value is not null)
            {
                row["Value"] = value["Value"];
                row["ValueCode"] = value.GetValueOrDefault("ValueCode")?.Trim();
            }
        }
        var header = Project(stored.Header, schema, "header");
        foreach (var key in new[] { "V_Type", "V_No", "GAmount", "TotalAmt", "ROffAmt", "NAmount" }) header[key] = stored.Header[key];
        var lines = stored.Lines.Select(line =>
        {
            var projected = Project(line, schema, "lines");
            projected["V_SNo"] = line["V_SNo"];
            projected["Amount"] = line["Amount"];
            return projected;
        }).ToList();
        await transaction.CommitAsync(ct);
        return new OrderResponse(id, stored.Version, schema.Version, header, lines,
            stored.HeaderExpenses, lineExpenses, termDefinitions, stored.LockReason is not null, stored.LockReason, RecordInfo:
            new(CleanActor("PreparedBy"), CleanActor("ModifiedBy"), CleanActor("ApprovedBy"), stored.ChangeLockReason),
            CanPrintPdf: options.CrystalSalesOrderSample && OperatingSystem.IsWindows() && session.Capabilities.Print);

        string? CleanActor(string key) => string.IsNullOrWhiteSpace(stored.Header.GetValueOrDefault(key)) ? null : stored.Header[key]!.Trim();
    }

    internal static string LegacyPrintArgument(string? persistedId)
    {
        // The legacy procedure concatenates a SQL IN-list. Only admit one quoted, ASCII ERP identifier.
        if (persistedId is null || !Regex.IsMatch(persistedId, @"\A[A-Za-z0-9 _./-]{1,48}\z",
                RegexOptions.CultureInvariant) || string.IsNullOrWhiteSpace(persistedId))
            throw new ApiFault(422, "unsupported_print_id", "This order reference cannot be printed by the legacy report.");
        return $"'{persistedId}'";
    }

    public async Task<DataSet> LoadPrintDataAsync(SessionResponse session, string id, CancellationToken ct)
    {
        Require(session.Capabilities.View && session.Capabilities.Print);
        if (!options.CrystalSalesOrderSample || !OperatingSystem.IsWindows())
            throw new ApiFault(503, "print_unavailable", "The Crystal Sales Order sample is disabled on this server.");

        var cutoff = await CutoffAsync(session, "view", ct);
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var header = await LoadHeaderAsync(connection, transaction, session, id, ct);
        CheckDate(session, header["V_Date"], cutoff);
        var mapping = await Database.QueryAsync(connection, transaction,
            "SELECT ReportName, ProcedureName FROM dbo.Voucher_Type WHERE V_Type=@voucher AND Category='SORD'",
            ct, ("@voucher", header["V_Type"]));
        if (mapping.Count != 1 ||
            !string.Equals(mapping[0]["ReportName"]?.Trim(), "SaleOrder", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(mapping[0]["ProcedureName"]?.Trim(), "ProcSaleOrder", StringComparison.OrdinalIgnoreCase))
            throw new ApiFault(422, "unsupported_print_mapping", "The voucher is not mapped to the supported Sales Order report.");

        await using var command = new SqlCommand("dbo.ProcSaleOrder", connection, transaction)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 15 };
        command.Parameters.Add("@SOrdDocId", SqlDbType.VarChar, 50).Value = LegacyPrintArgument(header["DocId"]?.TrimEnd());
        var data = new DataSet("SaleOrderData") { Locale = CultureInfo.InvariantCulture };
        try
        {
            var rows = data.Tables.Add("SaleOrderRows");
            string[] resultNames = ["ORDERNo", "V_Date", "party", "Item", "Qty", "Rate", "Amount"];
            string[] reportNames = ["OrderNo", "OrderDate", "Customer", "Item", "Qty", "Rate", "Amount"];
            foreach (var name in reportNames) rows.Columns.Add(name, typeof(string));
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                var ordinals = resultNames.Select(reader.GetOrdinal).ToArray();
                while (await reader.ReadAsync(ct))
                {
                    if (rows.Rows.Count >= 1000)
                        throw new ApiFault(422, "print_too_large", "This sample supports at most 1,000 order lines.");
                    rows.Rows.Add(ordinals.Select(column => reader.GetValue(column) switch
                    {
                        DBNull => "",
                        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        var value => Wire(value)?.Trim() ?? "",
                    }).ToArray());
                }
            }
            if (rows.Rows.Count == 0)
                throw new ApiFault(422, "print_no_data", "The Sales Order report procedure returned no rows.");
            await transaction.CommitAsync(ct);
            return data;
        }
        catch
        {
            data.Dispose();
            throw;
        }
    }

    public async Task<OrderResponse> CopyAsync(SessionResponse session, string id, CancellationToken ct)
    {
        Require(session.Capabilities.View && session.Capabilities.Add && !session.ReadOnly);
        var source = await GetAsync(session, id, ct);
        var fresh = await DefaultsAsync(session, ct);
        var template = await LineDefaultsAsync(session, ct);
        var schema = await metadata.GetAsync(session, ct);
        if (source.MetadataVersion != fresh.MetadataVersion || fresh.MetadataVersion != schema.Version)
            throw new ApiFault(409, "metadata_changed", "The field configuration changed. Reload before copying.");
        Dictionary<string, string?> CopyFields(Dictionary<string, string?> defaults, Dictionary<string, string?> values, string section) =>
            defaults.Where(pair => schema.Fields.Any(f => f.Section == section && f.Key == pair.Key))
                .Concat(schema.Fields.Where(f => f.Section == section && f.Writable &&
                !f.Type.Contains("date", StringComparison.OrdinalIgnoreCase) &&
                !(section == "header" && f.Key is "RecId" or "PartyOrdNo"))
                .Select(f => new KeyValuePair<string, string?>(f.Key, values.GetValueOrDefault(f.Key))))
                .GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value);
        static List<Dictionary<string, string?>> CopyExpenses(List<Dictionary<string, string?>> definitions, List<Dictionary<string, string?>> values) =>
            definitions.Select(def =>
            {
                var row = new Dictionary<string, string?>(def);
                var old = values.SingleOrDefault(value => value["SNo"] == def["SNo"] && value["ExpCode"] == def["ExpCode"]);
                if (old is not null)
                {
                    if (def["PerEditable"] == "Y") row["Per"] = old["Per"];
                    row["Amount"] = def["AmtEditable"] == "Y" ? old["Amount"] : "0";
                }
                return row;
            }).ToList();
        var header = CopyFields(fresh.Header, source.Header, "header");
        header["V_Type"] = NewOrderVoucher;
        header["PartyOrdNo"] = "";
        var terms = fresh.Terms.Select(def =>
        {
            var row = new Dictionary<string, string?>(def);
            var old = source.Terms.SingleOrDefault(term => term["Code"] == def["Code"]);
            if (old is not null)
            {
                row["Value"] = old["Value"];
                row["ValueCode"] = old.GetValueOrDefault("ValueCode");
            }
            return row;
        }).ToList();
        return fresh with
        {
            Header = header, Lines = source.Lines.Select(line => CopyFields(template.Values, line, "lines")).ToList(),
            HeaderExpenses = CopyExpenses(fresh.HeaderExpenses, source.HeaderExpenses),
            LineExpenses = source.LineExpenses.Select(expenses => CopyExpenses(template.Expenses, expenses)).ToList(), Terms = terms
        };
    }

    private static object? ValidateValue(FieldResponse field, string? value, string path)
    {
        try { return MetadataService.Validate(field, value); }
        catch (ApiFault fault) when (fault.Status == 422)
        {
            throw ApiFault.At(fault.Status, fault.Code, fault.Message, path);
        }
    }

    private static Dictionary<string, object?> ValidateInput(Dictionary<string, string?> input, SchemaResponse schema,
        string section, string? path = null)
    {
        var fields = schema.Fields.Where(f => f.Section == section).ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        if (input.Keys.Any(k => !fields.ContainsKey(k)))
            throw new ApiFault(422, "unknown_field", $"Unknown or protected {section} field.");
        return input.ToDictionary(p => p.Key, p => ValidateValue(fields[p.Key], p.Value, $"{path ?? section}.{fields[p.Key].Key}"),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task WriteRowAsync(SqlConnection connection, SqlTransaction transaction, string table,
        Dictionary<string, object?> values, Dictionary<string, object?>? keys, CancellationToken ct)
    {
        // All identifiers originate in fixed server mappings or approved metadata, never request keys.
        static string Identifier(string value) => "[" + value.Replace("]", "]]") + "]";
        var parameters = values.Select((p, i) => ($"@p{i}", p.Value)).ToList();
        string sql;
        if (keys is null)
            sql = $"INSERT dbo.{Identifier(table)} ({string.Join(",", values.Keys.Select(Identifier))}) VALUES({string.Join(",", parameters.Select(p => p.Item1))})";
        else
        {
            var assignments = values.Keys.Select((k, i) => $"{Identifier(k)}=@p{i}");
            var conditions = keys.Keys.Select((k, i) => $"{Identifier(k)}=@k{i}");
            parameters.AddRange(keys.Select((p, i) => ($"@k{i}", p.Value)));
            sql = $"UPDATE dbo.{Identifier(table)} SET {string.Join(",", assignments)} WHERE {string.Join(" AND ", conditions)}";
        }
        var affected = await Database.ExecuteAsync(connection, transaction, sql, ct, parameters.ToArray());
        if (affected != 1) throw new ApiFault(409, "write_conflict", "The record changed. Reload and retry.");
    }

    private async Task ValidateLookupsAsync(SqlConnection connection, SqlTransaction transaction,
        Dictionary<string, object?> header, List<Dictionary<string, object?>> lines, SessionResponse session, CancellationToken ct)
    {
        foreach (var (row, index) in lines.Prepend(header).Select((row, index) => (row, index)))
        {
            foreach (var (key, table, column) in new[] { ("PartyCode", "SubGroup", "SubCode"), ("DelAt", "SubGroup", "SubCode"),
                ("Consignee", "SubGroup", "SubCode"), ("FormCode", "TaxForms", "Form_Code"), ("ItemCode", "ItemMast", "Code"),
                ("BF", "BFMast", "Code"), ("GSM", "GSMMast", "Code") })
            {
                if (!row.TryGetValue(key, out var value) || value is null) continue;
                var site = table is "SubGroup" or "ItemMast"
                    ? " AND (NULLIF(SiteList,'') IS NULL OR CHARINDEX('|' + @site + '|','|' + SiteList + '|')>0)" : "";
                var match = await Database.QueryAsync(connection, transaction,
                    $"SELECT [{column}] FROM dbo.[{table}] WHERE [{column}]=@value{site}", ct,
                    ("@value", value), ("@site", session.Site));
                if (match.Count != 1) throw ApiFault.At(422, "invalid_lookup", $"{key} is not available in this context.",
                    index == 0 ? $"header.{key}" : $"lines[{index - 1}].{key}");
            }
        }
    }

    private static void SetCalculatedLine(Dictionary<string, object?> line, SalesLineResult result)
    {
        line["Qty"] = result.Quantity; line["Reel"] = result.Reels;
        line["SizeCM"] = result.SizeCm; line["SizeInch"] = result.SizeInch; line["Amount"] = result.Amount;
    }

    private static async Task<SalesCalculationSettings> CalculationSettingsAsync(SqlConnection connection,
        SqlTransaction transaction, SessionResponse session, CancellationToken ct)
    {
        var environment = await Database.QueryAsync(connection, transaction,
            "SELECT InchKG,CMKG,PriceApplicableYN,Sl_Order_Weight_round_to_places FROM dbo.Enviro WHERE Site_Code=@site",
            ct, ("@site", session.Site));
        if (environment.Count != 1 || environment[0]["PriceApplicableYN"] != "N")
            throw new ApiFault(422, "unsupported_pricing", "This POC currently requires the discovered manual-price SORD configuration.");
        var places = ExpenseCalculator.Number(environment[0], "Sl_Order_Weight_round_to_places");
        if (places != decimal.Truncate(places) || places is < 0 or > 3)
            throw new ApiFault(422, "unsupported_rounding", "Sales weight rounding must be configured between 0 and 3 decimal places.");
        return new(ExpenseCalculator.Number(environment[0], "InchKG"), ExpenseCalculator.Number(environment[0], "CMKG"), (int)places);
    }

    public Task<ToleranceResponse> CalculateToleranceAsync(SessionResponse session, ToleranceRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Require(session.Capabilities.View);
        return Task.FromResult(QuantityToleranceCalculator.Calculate(request));
    }

    public async Task<DraftCalculationResponse> CalculateDraftAsync(SessionResponse session, DraftCalculationRequest request, CancellationToken ct)
    {
        Require(session.Capabilities.View && (session.Capabilities.Add || session.Capabilities.Edit) && !session.ReadOnly);
        if (request.Lines.Count > 100 || request.LineExpenses.Count != request.Lines.Count)
            throw new ApiFault(422, "invalid_order", "Draft calculations support up to 100 lines with matching expenses.");
        var schema = await metadata.GetAsync(session, ct);
        if (schema.Version != request.MetadataVersion)
            throw new ApiFault(409, "metadata_changed", "Field definitions changed. Reload metadata before calculating.");
        Require(request.DocumentId is null ? session.Capabilities.Add : session.Capabilities.Edit);
        var viewCutoff = request.DocumentId is null ? null : await CutoffAsync(session, "view", ct);
        var partialSchema = schema with { Fields = schema.Fields.Select(field => field with { Required = false }).ToList() };
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var settings = await CalculationSettingsAsync(connection, transaction, session, ct);
        var stored = request.DocumentId is null ? null : await LoadAsync(connection, transaction, session, request.DocumentId, ct);
        if (stored is not null) CheckDate(session, stored.Header["V_Date"], viewCutoff);
        if (stored?.LockReason is not null) throw new ApiFault(409, "order_locked", stored.LockReason);
        var voucher = stored is null ? await ResolveVoucherAsync(connection, transaction, request.VoucherType, ct)
            : stored.Header["V_Type"]!.Trim();
        if (stored is not null && request.VoucherType is not null && !string.Equals(voucher, request.VoucherType, StringComparison.Ordinal))
            throw new ApiFault(422, "invalid_voucher", "A saved order's voucher type cannot be changed.");
        var lineDefinitions = await DefinitionsAsync(connection, transaction, true, ct, voucher);
        var headerDefinitions = stored?.HeaderExpenses ?? await DefinitionsAsync(connection, transaction, false, ct, voucher);
        var lines = new List<Dictionary<string, string?>>();
        var expenses = new List<List<Dictionary<string, string?>>>();
        decimal gross = 0;
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var oldNumber = request.Lines[index].GetValueOrDefault("V_SNo");
            if (oldNumber is not null && (stored is null || !stored.Lines.Any(line => line["V_SNo"] == oldNumber)))
                throw new ApiFault(422, "unknown_line", "Unknown line identity.");
            var values = ValidateInput(request.Lines[index].Where(p => p.Key != "V_SNo").ToDictionary(), partialSchema, "lines", $"lines[{index}]");
            var text = values.ToDictionary(p => p.Key, p => Wire(p.Value), StringComparer.OrdinalIgnoreCase);
            var itemCode = text.GetValueOrDefault("ItemCode");
            decimal conversion = 0;
            if (!string.IsNullOrWhiteSpace(itemCode))
            {
                var items = await Database.QueryAsync(connection, transaction,
                    """
                    SELECT ConversionRate FROM dbo.ItemMast WHERE Code=@item
                      AND (NULLIF(SiteList,'') IS NULL OR CHARINDEX('|' + @site + '|','|' + SiteList + '|')>0)
                    """, ct, ("@item", itemCode), ("@site", session.Site));
                if (items.Count != 1) throw ApiFault.At(422, "invalid_lookup", "Select an item available in this workspace.", $"lines[{index}].ItemCode");
                conversion = ExpenseCalculator.Number(items[0], "ConversionRate");
            }
            var result = SalesLineCalculator.Calculate(text, settings, conversion, $"lines[{index}]", false, index + 1);
            var calculatedValues = new Dictionary<string, object?>();
            SetCalculatedLine(calculatedValues, result);
            var configuration = oldNumber is null ? lineDefinitions : VoucherExpenseStore.ForLine(stored!.LineExpenses, oldNumber);
            var oldItem = stored?.Lines.SingleOrDefault(line => line["V_SNo"] == oldNumber)?.GetValueOrDefault("ItemCode");
            if (!string.IsNullOrWhiteSpace(itemCode) && !string.Equals(oldItem, itemCode, StringComparison.OrdinalIgnoreCase))
                configuration = VoucherExpenseStore.ApplyItemDefaults(configuration,
                    await VoucherExpenseStore.LoadItemDefaultsAsync(connection, transaction, itemCode, session.Site, ct));
            var calculatedExpenses = ExpenseCalculator.Calculate(configuration, request.LineExpenses[index],
                result.Amount, $"lineExpenses[{index}]", false);
            ApplyExpenseTargets(calculatedValues, calculatedExpenses, "SOrdDet");
            lines.Add(calculatedValues.ToDictionary(p => p.Key, p => Wire(p.Value)));
            expenses.Add(calculatedExpenses);
            gross += result.Amount;
        }
        var headerExpenses = ExpenseCalculator.CalculateHeader(headerDefinitions, request.HeaderExpenses,
            stored is null ? lineDefinitions : stored.LineExpenses, expenses, gross, false);
        var header = new Dictionary<string, object?>();
        ApplyExpenseTargets(header, headerExpenses, "SOrd");
        await transaction.RollbackAsync(ct);
        return new(header.ToDictionary(p => p.Key, p => Wire(p.Value)), lines, headerExpenses, expenses);
    }

    public async Task<OrderResponse> SaveAsync(SessionResponse session, string? id, SaveOrderRequest request, bool preview, CancellationToken ct)
    {
        if (request.VoucherType is not null && request.VoucherType != NewOrderVoucher)
            throw new ApiFault(422, "voucher_preview_only", "Only the SORD voucher can be saved or fully validated in this portal.");
        if (session.ReadOnly) throw new ApiFault(403, "read_only_context", "This company/year/site is read-only in the POC.");
        Require(id is null ? session.Capabilities.Add : session.Capabilities.Edit);
        if (!options.DevelopmentWritesApproved && !preview)
            throw new ApiFault(403, "writes_not_approved", "Development database writes are disabled.");
        if (request.Lines.Count is < 1 or > 100 || !Guid.TryParse(request.IdempotencyKey, out _))
            throw new ApiFault(422, "invalid_order", "An order requires 1-100 lines and a UUID idempotency key.");
        if (id is not null && request.AutomaticReference)
            throw new ApiFault(422, "invalid_numbering", "Automatic references are only available for new documents.");
        var schema = await metadata.GetAsync(session, ct);
        if (request.MetadataVersion != schema.Version)
            throw new ApiFault(409, "metadata_changed", "Field definitions changed. Reload metadata before saving.");
        var header = ValidateInput(request.Header, schema, "header");
        var cutoff = await CutoffAsync(session, "save", ct);
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockWritesAsync(connection, transaction, session, ct);
        var digest = Hash(new { id, request });
        var commandKey = Hash(new { session.Username, session.Company, session.Site, session.Prefix, request.IdempotencyKey });
        var prior = await Database.QueryAsync(connection, transaction,
            "SELECT PayloadHash,DocId FROM webpoc.Command WITH(UPDLOCK,HOLDLOCK) WHERE CommandKey=@key", ct, ("@key", commandKey));
        if (!preview && prior.Count != 0)
        {
            if (prior[0]["PayloadHash"] != digest) throw new ApiFault(409, "idempotency_conflict", "This request key was already used with a different payload.");
            var priorId = prior[0]["DocId"]!;
            var existing = await Database.QueryAsync(connection, transaction, "SELECT DocId FROM dbo.SOrd WHERE DocId=@id", ct, ("@id", priorId));
            if (existing.Count == 0) throw new ApiFault(409, "idempotency_deleted", "The order created by this request was deleted. This request key cannot be reused.");
            await transaction.RollbackAsync(ct);
            return await GetAsync(session, priorId, ct);
        }
        StoredOrder? stored = id is null ? null : await LoadAsync(connection, transaction, session, id, ct);
        if (stored is not null)
        {
            if (stored.Version != request.Version) throw new ApiFault(409, "stale_order", "The order changed. Reload before editing.");
            if (stored.LockReason is not null) throw new ApiFault(409, "order_locked", stored.LockReason);
            CheckDate(session, stored.Header["V_Date"], cutoff);
            if (!string.IsNullOrWhiteSpace(stored.Header.GetValueOrDefault("PL_DociD")))
                throw new ApiFault(422, "selected_price_list", "Editing existing selected-price-list orders requires the deferred price-list workflow.");
            foreach (var field in schema.Fields.Where(f => f.Section == "header"))
                if (!header.ContainsKey(field.Key)) header[field.Key] = ValidateValue(field, stored.Header.GetValueOrDefault(field.Key), $"header.{field.Key}");
        }
        foreach (var field in schema.Fields.Where(f => f.Section == "header" && f.Required))
            if (header.GetValueOrDefault(field.Key) is null)
                throw ApiFault.At(422, "required_field", $"{field.Label} is required.", $"header.{field.Key}");
        var orderDate = Convert.ToDateTime(header["V_Date"], CultureInfo.InvariantCulture);
        CheckDate(session, orderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cutoff);
        var voucher = await Database.QueryAsync(connection, transaction,
            "SELECT AcceptEntryAfter FROM dbo.Voucher_Type WHERE V_Type='SORD' AND Category='SORD' AND ISNULL(ActiveYN,'Y')='Y'", ct);
        if (voucher.Count != 1 || voucher[0]["AcceptEntryAfter"] is { Length: > 0 } accept && orderDate <= DateTime.Parse(accept, CultureInfo.InvariantCulture))
            throw ApiFault.At(422, "voucher_closed", "This voucher/date is not open for entry.", "header.V_Date");
        var settings = await CalculationSettingsAsync(connection, transaction, session, ct);
        var lineDefinitions = await DefinitionsAsync(connection, transaction, true, ct);
        var headerDefinitions = stored?.HeaderExpenses ?? await DefinitionsAsync(connection, transaction, false, ct);
        var termBindings = new Dictionary<string, TermLookupSource>();
        var terms = await TermDefinitionsAsync(connection, transaction, ct, bindings: termBindings);
        var lineRows = new List<Dictionary<string, object?>>();
        var lineExpenses = new List<List<Dictionary<string, string?>>>();
        decimal gross = 0;
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = ValidateInput(request.Lines[index].Where(p => p.Key != "V_SNo")
                .ToDictionary(p => p.Key, p => p.Value), schema, "lines", $"lines[{index}]");
            var oldNumber = request.Lines[index].GetValueOrDefault("V_SNo");
            var old = oldNumber is null ? null : stored?.Lines.SingleOrDefault(l => l["V_SNo"] == oldNumber);
            if (oldNumber is not null && old is null) throw new ApiFault(422, "unknown_line", "Unknown line identity.");
            foreach (var field in schema.Fields.Where(f => f.Section == "lines"))
                if (!line.ContainsKey(field.Key)) line[field.Key] = ValidateValue(field, old?.GetValueOrDefault(field.Key), $"lines[{index}].{field.Key}");
            var text = line.ToDictionary(p => p.Key, p => Convert.ToString(p.Value, CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
            var item = await Database.QueryAsync(connection, transaction,
                "SELECT ConversionRate,ItemType FROM dbo.ItemMast WHERE Code=@item", ct, ("@item", line.GetValueOrDefault("ItemCode")));
            var retainedItem = old is not null && string.Equals(old.GetValueOrDefault("ItemCode"),
                text.GetValueOrDefault("ItemCode"), StringComparison.OrdinalIgnoreCase);
            if (item.Count == 1 && !string.Equals(item[0]["ItemType"], "FM", StringComparison.OrdinalIgnoreCase) && !retainedItem)
                throw ApiFault.At(422, "finished_product_required",
                    $"Row {index + 1}: select a Finished Product item. Only Item master type FM is available for new or replaced items.",
                    $"lines[{index}].ItemCode");
            var secondary = ExpenseCalculator.Number(text, "QtySDU");
            if (secondary > 0)
            {
                if (item.Count != 1) throw ApiFault.At(422, "invalid_lookup", "Select a valid item.", $"lines[{index}].ItemCode");
            }
            var result = SalesLineCalculator.Calculate(text, settings,
                item.Count == 1 ? ExpenseCalculator.Number(item[0], "ConversionRate") : 0, $"lines[{index}]", true, index + 1);
            QuantityToleranceCalculator.Validate(result.Quantity, text, $"lines[{index}]");
            SetCalculatedLine(line, result);
            line["V_SNo"] = oldNumber is null ? 0 : int.Parse(oldNumber, CultureInfo.InvariantCulture);
            var input = index < request.LineExpenses.Count ? request.LineExpenses[index] : [];
            var lineConfiguration = oldNumber is null ? lineDefinitions : VoucherExpenseStore.ForLine(stored!.LineExpenses, oldNumber);
            if (!retainedItem && text.GetValueOrDefault("ItemCode") is { Length: > 0 } itemCode)
                lineConfiguration = VoucherExpenseStore.ApplyItemDefaults(lineConfiguration,
                    await VoucherExpenseStore.LoadItemDefaultsAsync(connection, transaction, itemCode, session.Site, ct));
            var calculated = ExpenseCalculator.Calculate(lineConfiguration, input, result.Amount, $"lineExpenses[{index}]");
            ApplyExpenseTargets(line, calculated, "SOrdDet");
            lineExpenses.Add(calculated); lineRows.Add(line); gross += result.Amount;
        }
        if (request.LineExpenses.Count > lineRows.Count)
            throw new ApiFault(422, "invalid_expense", "Expense inputs do not match line count.");
        var usedNumbers = lineRows.Select(l => (int)l["V_SNo"]!).Where(n => n != 0).ToArray();
        if (usedNumbers.Distinct().Count() != usedNumbers.Length)
            throw new ApiFault(422, "duplicate_line", "Duplicate line identity.");
        var headerExpenses = ExpenseCalculator.CalculateHeader(headerDefinitions, request.HeaderExpenses,
            stored is null ? lineDefinitions : stored.LineExpenses, lineExpenses, gross, true);
        ApplyExpenseTargets(header, headerExpenses, "SOrd");
        var savedTermKeys = stored?.Terms.ToLookup(row => row["TCMast_Code"], row => row.GetValueOrDefault("ValueCode")?.Trim());
        var termCodesByLookup = terms.Where(term => term.ContainsKey("LookupId")).ToDictionary(term => term["LookupId"]!, term => term["Code"]);
        await VoucherTerms.ApplyAsync(terms, request.Terms, "SOrd", (lookupId, key) =>
            VoucherTerms.ResolveAsync(connection, transaction, termBindings[lookupId], key, ct,
                retained: savedTermKeys?[termCodesByLookup[lookupId]].Contains(key, StringComparer.Ordinal) == true));
        await ValidateLookupsAsync(connection, transaction, header, lineRows, session, ct);
        if (preview)
        {
            var previewNumbers = id is null
                ? await DocumentNumbering.NextAsync(connection, transaction, "SOrd", session.Site, "SORD", session.Prefix, ct)
                : null;
            if (request.AutomaticReference) header["RecId"] = previewNumbers!.Reference;
            if (stored is not null) header["V_No"] = stored.Header["V_No"];
            await transaction.RollbackAsync(ct);
            return new OrderResponse(id, request.Version, schema.Version,
                header.ToDictionary(p => p.Key, p => Wire(p.Value)),
                lineRows.Select(l => l.ToDictionary(p => p.Key, p => Wire(p.Value))).ToList(),
                headerExpenses, lineExpenses, terms, false, null, previewNumbers);
        }
        var nextLine = stored?.Lines.Select(l => int.Parse(l["V_SNo"]!, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() ?? 0;
        foreach (var line in lineRows.Where(l => (int)l["V_SNo"]! == 0)) line["V_SNo"] = ++nextLine;
        var numbers = await DocumentNumbering.NextAsync(connection, transaction, "SOrd", session.Site, "SORD", session.Prefix, ct);
        var number = stored?.Header["V_No"] ?? numbers.VoucherNumber;
        id ??= SalesRules.DocumentId(session.Site, "SORD", session.Prefix, long.Parse(number, CultureInfo.InvariantCulture));
        var reference = (request.AutomaticReference ? numbers.Reference :
            header.GetValueOrDefault("RecId")?.ToString() ?? numbers.Reference).Replace('|', '-').Trim();
        if (reference.Length is < 1 or > 10) throw ApiFault.At(422, "invalid_reference", "Order reference must contain 1-10 characters.", "header.RecId");
        var duplicate = await Database.QueryAsync(connection, transaction,
            "SELECT DocId FROM dbo.SOrd WHERE RecId=@reference AND DocId<>@id AND V_Prefix=@prefix AND Site_Code=@site AND V_Type='SORD'",
            ct, ("@reference", reference), ("@id", id), ("@prefix", session.Prefix), ("@site", session.Site));
        if (duplicate.Count > 0) throw ApiFault.At(409, "duplicate_reference", "Order reference already exists.", "header.RecId");
        header["RecId"] = reference; header["ModifiedBy"] = session.Username;
        header["U_EntDt"] = DateTime.Now; header["U_AE"] = stored is null ? "A" : "E";
        if (stored is null)
        {
            header["DocId"] = id; header["V_Type"] = "SORD"; header["V_No"] = number;
            header["V_Prefix"] = int.Parse(session.Prefix, CultureInfo.InvariantCulture);
            header["Site_Code"] = session.Site; header["PreparedBy"] = session.Username;
        }
        await WriteRowAsync(connection, transaction, "SOrd", header,
            stored is null ? null : new() { ["DocId"] = id }, ct);
        checkpoint.Reached("header");
        var retained = lineRows.Select(l => (int)l["V_SNo"]!).ToHashSet();
        foreach (var removed in stored?.Lines ?? [])
            if (!retained.Contains(int.Parse(removed["V_SNo"]!, CultureInfo.InvariantCulture)))
                await Database.ExecuteAsync(connection, transaction, "DELETE dbo.SOrdDet WHERE DocId=@id AND V_SNo=@line",
                    ct, ("@id", id), ("@line", int.Parse(removed["V_SNo"]!, CultureInfo.InvariantCulture)));
        for (var index = 0; index < lineRows.Count; index++)
        {
            var line = lineRows[index]; var lineNumber = (int)line["V_SNo"]!;
            var existing = stored?.Lines.Any(l => l["V_SNo"] == lineNumber.ToString(CultureInfo.InvariantCulture)) == true;
            line["RecId"] = reference; line["V_Date"] = orderDate;
            if (!existing)
            {
                line["DocId"] = id; line["V_Type"] = "SORD"; line["Site_Code"] = session.Site;
                line["V_Prefix"] = int.Parse(session.Prefix, CultureInfo.InvariantCulture); line["Closed"] = "N";
            }
            var keys = new Dictionary<string, object?> { ["DocId"] = id, ["V_SNo"] = lineNumber };
            await WriteRowAsync(connection, transaction, "SOrdDet", line, existing ? keys : null, ct);
        }
        checkpoint.Reached("details");
        await Database.ExecuteAsync(connection, transaction,
            "DELETE dbo.SPExpenseLine WHERE DocId=@id; DELETE dbo.SPExpense WHERE DocId=@id; DELETE dbo.Term_Cond_Trans WHERE Ref_DocId=@id;",
            ct, ("@id", id));
        foreach (var expense in headerExpenses) await SaveExpenseAsync(connection, transaction, expense, id, null, session.Site, ct);
        for (var index = 0; index < lineRows.Count; index++)
            foreach (var expense in lineExpenses[index])
                await SaveExpenseAsync(connection, transaction, expense, id, (int)lineRows[index]["V_SNo"]!, session.Site, ct);
        checkpoint.Reached("expenses");
        foreach (var term in terms)
            await WriteRowAsync(connection, transaction, "Term_Cond_Trans",
                VoucherTerms.Snapshot(term, id, NewOrderVoucher, "SOrd"), null, ct);
        checkpoint.Reached("terms");
        await Database.ExecuteAsync(connection, transaction,
            """
            INSERT webpoc.Command(CommandKey,Username,PayloadHash,DocId) VALUES(@key,@user,@hash,@id);
            INSERT webpoc.Audit(Username,Operation,RecordId) VALUES(@user,@operation,@id);
            """, ct, ("@key", commandKey), ("@user", session.Username), ("@hash", digest), ("@id", id),
            ("@operation", stored is null ? "order.create" : "order.update"));
        var submitted = await LoadAsync(connection, transaction, session, id, ct);
        var creator = submitted.Header.GetValueOrDefault("PreparedBy");
        await tasks.SubmitDocumentAsync(connection, transaction, session, "SORD", id, reference, submitted.Version,
            string.IsNullOrWhiteSpace(creator) ? session.Username : creator.Trim(), commandKey, stored is null, ct);
        checkpoint.Reached("tasks");
        await transaction.CommitAsync(ct);
        return await GetAsync(session, id, ct);
    }

    private static readonly HashSet<string> TaxExpenseTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "CGSTPer", "CGSTAmt", "SGSTPer", "SGSTAmt", "IGSTPer", "IGSTAmt",
        "UTGSTPer", "UTGSTAmt", "CESSPer", "CESSAmt"
    };

    internal static void ApplyExpenseTargets(Dictionary<string, object?> target, List<Dictionary<string, string?>> expenses, string table)
    {
        var permitted = table == "SOrd" ? new[] { "GAmount", "TotalAmt", "ROffAmt", "NAmount" } : ["Amount", "AgreedRate"];
        foreach (var row in expenses)
        {
            if (row["TableName"] != table || !string.IsNullOrWhiteSpace(row.GetValueOrDefault("Account")) ||
                !string.IsNullOrWhiteSpace(row.GetValueOrDefault("ContraExp")) ||
                !string.IsNullOrWhiteSpace(row.GetValueOrDefault("DrCr")))
                throw new ApiFault(422, "unsupported_posting", "This configured posting is outside the approved POC.");
            foreach (var (source, mapping) in new[] { ("Per", "Per_SaveIn"), ("Amount", "Amt_SaveIn") })
            {
                if (string.IsNullOrWhiteSpace(row.GetValueOrDefault(mapping))) continue;
                var field = row[mapping]!;
                var canonicalTaxField = TaxExpenseTargets.TryGetValue(field.Trim(), out var taxField) ? taxField : null;
                if (!permitted.Contains(field) && (canonicalTaxField is null ||
                    !canonicalTaxField.EndsWith(source == "Per" ? "Per" : "Amt", StringComparison.Ordinal)))
                    throw new ApiFault(422, "unsupported_mapping", "An expense targets an unsupported business field.");
                target[canonicalTaxField ?? field] = ExpenseCalculator.Number(row, source);
            }
        }
    }

    private static async Task SaveExpenseAsync(SqlConnection connection, SqlTransaction transaction,
        Dictionary<string, string?> row, string id, int? line, string site, CancellationToken ct)
    {
        var keys = new[] { "SNo", "ExpCode", "ExpShortCode", "Per", "Amount", "ExpType", "FormulaString",
            "Per_SaveIn", "Amt_SaveIn", "TableName", "PerEditable", "AmtEditable", "FullValue", "RoundOff", "MandatoryPer", "MandatoryAmt" };
        var values = keys.ToDictionary(k => k, k => (object?)row.GetValueOrDefault(k), StringComparer.OrdinalIgnoreCase);
        values["DocId"] = id; values["Site_Code"] = site;
        if (line is not null) values["V_SNo"] = line.Value;
        else
            foreach (var key in new[] { "Account", "DrCr", "AcEditable", "ContraExp", "ActualFormula", "ExcludeFormula", "CalLValOn", "UniqueId" })
                values[key] = row.GetValueOrDefault(key);
        await WriteRowAsync(connection, transaction, line is null ? "SPExpense" : "SPExpenseLine", values, null, ct);
    }

    public async Task DeleteAsync(SessionResponse session, string id, DeleteOrderRequest request, CancellationToken ct)
    {
        if (session.ReadOnly) throw new ApiFault(403, "read_only_context", "This company/year/site is read-only in the POC.");
        Require(session.Capabilities.Delete && options.DevelopmentWritesApproved);
        var cutoff = await CutoffAsync(session, "delete", ct);
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockWritesAsync(connection, transaction, session, ct);
        var stored = await LoadAsync(connection, transaction, session, id, ct);
        CheckDate(session, stored.Header["V_Date"], cutoff);
        if (stored.Version != request.Version) throw new ApiFault(409, "stale_order", "The order changed. Reload before deleting.");
        if (stored.LockReason is not null) throw new ApiFault(409, "order_locked", stored.LockReason);
        await tasks.CancelDocumentAsync(connection, transaction, session, id, ct);
        await Database.ExecuteAsync(connection, transaction,
            """
            DELETE dbo.Term_Cond_Trans WHERE Ref_DocId=@id;
            DELETE dbo.SPExpenseLine WHERE DocId=@id; DELETE dbo.SPExpense WHERE DocId=@id;
            DELETE dbo.SOrdDet WHERE DocId=@id; DELETE dbo.SOrd WHERE DocId=@id;
            INSERT webpoc.Audit(Username,Operation,RecordId) VALUES(@user,'order.delete',@id);
            """, ct, ("@id", id), ("@user", session.Username));
        checkpoint.Reached("delete");
        await transaction.CommitAsync(ct);
    }

    public async Task<ApprovalStateResponse> ApprovalAsync(SessionResponse session, string id, CancellationToken ct)
    {
        var order = await GetAsync(session, id, ct);
        await using var connection = await database.OpenAsync(false, ct);
        var state = await tasks.ApprovalAsync(connection, null, session, id, session.Capabilities.Approve, ct);
        return state with
        {
            CanAct = state.CanAct && !order.Locked,
            CanReverse = order.RecordInfo?.ApprovedBy is not null && order.RecordInfo.ChangeBlockedReason is null &&
                await tasks.CanReverseAsync(connection, null, session, "SORD", ct)
        };
    }

    public async Task<OrderResponse> DecideAsync(SessionResponse session, string id, OrderDecisionRequest request, CancellationToken ct)
    {
        Require(session.Capabilities.View && session.Capabilities.Approve && !session.ReadOnly && options.DevelopmentWritesApproved);
        var viewCutoff = await CutoffAsync(session, "view", ct);
        var saveCutoff = await CutoffAsync(session, "save", ct);
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockWritesAsync(connection, transaction, session, ct);
        var stored = await LoadAsync(connection, transaction, session, id, ct);
        CheckDate(session, stored.Header["V_Date"], viewCutoff);
        CheckDate(session, stored.Header["V_Date"], saveCutoff);
        if (stored.Version != request.Version) throw new ApiFault(409, "stale_order", "The order changed. Reload before deciding.");
        if (stored.LockReason is not null) throw new ApiFault(409, "order_locked", stored.LockReason);
        var final = await tasks.DecideAsync(connection, transaction, session, id, stored.Version, request, session.Capabilities.Approve, ct);
        if (final)
            await Database.ExecuteAsync(connection, transaction, "UPDATE dbo.SOrd SET ApprovedBy=@user WHERE DocId=@id",
                ct, ("@user", session.Username), ("@id", id));
        await Database.ExecuteAsync(connection, transaction,
            "INSERT webpoc.Audit(Username,Operation,RecordId) VALUES(@user,@operation,@id)", ct,
            ("@user", session.Username), ("@operation", request.Decision == "Reject" ? "order.reject" : final ? "order.approve" : "order.approve.stage"), ("@id", id));
        checkpoint.Reached("approval");
        await transaction.CommitAsync(ct);
        return await GetAsync(session, id, ct);
    }

    public async Task<OrderResponse> UnapproveAsync(SessionResponse session, string id, UnapproveOrderRequest request, CancellationToken ct)
    {
        Require(session.Capabilities.View && session.Capabilities.Approve && !session.ReadOnly && options.DevelopmentWritesApproved);
        var viewCutoff = await CutoffAsync(session, "view", ct);
        var saveCutoff = await CutoffAsync(session, "save", ct);
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockWritesAsync(connection, transaction, session, ct);
        var stored = await LoadAsync(connection, transaction, session, id, ct);
        CheckDate(session, stored.Header["V_Date"], viewCutoff);
        CheckDate(session, stored.Header["V_Date"], saveCutoff);
        if (stored.Version != request.Version) throw new ApiFault(409, "stale_order", "The order changed. Reload before unapproving.");
        if (stored.ChangeLockReason is not null) throw new ApiFault(409, "order_locked", stored.ChangeLockReason);
        var previousApprover = stored.Header.GetValueOrDefault("ApprovedBy")?.Trim();
        if (string.IsNullOrEmpty(previousApprover)) throw new ApiFault(409, "not_approved", "This order is not approved.");
        await tasks.ReverseDocumentAsync(connection, transaction, session, "SORD", id, stored.Header["RecId"]!,
            stored.Header.GetValueOrDefault("PreparedBy")?.Trim() is { Length: > 0 } creator ? creator : session.Username,
            previousApprover, request, ct);
        await Database.ExecuteAsync(connection, transaction, """
            UPDATE dbo.SOrd SET ApprovedBy=NULL WHERE DocId=@id;
            INSERT webpoc.Audit(Username,Operation,RecordId) VALUES(@user,'order.unapprove',@id);
            """, ct, ("@id", id), ("@user", session.Username));
        checkpoint.Reached("unapproval");
        await transaction.CommitAsync(ct);
        return await GetAsync(session, id, ct);
    }

    private static async Task LockWritesAsync(SqlConnection connection, SqlTransaction transaction, SessionResponse session, CancellationToken ct)
    {
        await using var command = Database.Command(connection, transaction,
            """
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=60000;
            IF @result<0 THROW 50000, 'Could not acquire order command lock.', 1;
            """, ("@resource", $"PaperWebPOC:{session.Company}:{session.Site}:{session.Prefix}:SORD"));
        command.CommandTimeout = 65;
        await command.ExecuteNonQueryAsync(ct);
    }
}
