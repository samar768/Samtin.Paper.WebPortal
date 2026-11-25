using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text.RegularExpressions;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

internal static class VoucherTerms
{
    internal const string LookupPrefix = "terms-";

    private static string Identifier(string name) => Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z")
        ? $"[{name}]"
        : throw new ApiFault(409, "lookup_configuration", "A term lookup identifier requires review.");

    internal static async Task<(string? Code, string? Name)> ResolveAsync(SqlConnection connection,
        SqlTransaction transaction, TermLookupSource source, string key, CancellationToken ct, bool retained = false)
    {
        if (source.KeyLength is < 1 or > 50 || key.Length > source.KeyLength) return (null, null);
        var rows = await Database.QueryAsync(connection, transaction,
            $"SELECT TOP(2) {Identifier(source.Key)} AS Code,{Identifier(source.Label)} AS Name FROM {(retained ? source.Relation : source.Rows)} s WHERE {Identifier(source.Key)}=@code",
            ct, ("@code", key), ("@termLimit", source.Limit));
        return rows.Count == 1 ? (rows[0]["Code"]?.TrimEnd(), rows[0]["Name"]) : (null, null);
    }

    internal static async Task<List<Dictionary<string, string?>>> LoadAsync(SqlConnection connection,
        SqlTransaction? transaction, string voucher, CancellationToken ct,
        Dictionary<string, TermLookupSource>? bindings, string resource, string voucherCategory)
    {
        var rows = await Database.QueryAsync(connection, transaction,
            """
            SELECT d.Code,d.SNo,d.VType,d.TableName,d.SaveIn,d.Mandatory,d.DefaultValue,d.DMHGQuery,
                ISNULL(d.TCFieldDataType,3) AS TCFieldDataType,m.TCName AS Label,m.TCCode AS TCManualCode,
                d.DefaultValue AS Value
            FROM dbo.Term_Cond_Det d JOIN dbo.Term_Cond_Mast m ON m.Code=d.Code
            JOIN dbo.Voucher_Type v ON v.V_Type=d.VType
            WHERE d.VType=@voucher AND v.Category=@category ORDER BY d.SNo,d.Code
            """, ct, ("@voucher", voucher), ("@category", voucherCategory));
        foreach (var row in rows)
        {
            row["Editor"] = row["TCFieldDataType"] switch
            {
                "0" => "date", "1" => "integer", "2" => "decimal", "3" => "text", "5" => "lookup", _ => "unsupported"
            };
            if (row["Editor"] == "lookup" && VoucherTermLookup.Parse(row["DMHGQuery"]) is { } definition &&
                await VoucherTermLookup.BindAsync(connection, transaction, definition, resource, ct) is { } source)
            {
                var id = $"{LookupPrefix}{row["VType"]?.Trim()}-{row["Code"]}";
                if (!Regex.IsMatch(id, @"\A[a-zA-Z0-9_-]{1,60}\z"))
                    throw new ApiFault(409, "lookup_configuration", "A term lookup identifier requires review.");
                row["LookupId"] = id;
                if (bindings is not null && !bindings.TryAdd(id, source))
                    throw new ApiFault(409, "lookup_configuration", "The configured term lookup is ambiguous.");
            }
            row.Remove("DMHGQuery");
        }
        return rows;
    }

    internal static async Task ApplyAsync(List<Dictionary<string, string?>> terms, List<Dictionary<string, string?>> inputs,
        string documentTable, Func<string, string, Task<(string? Code, string? Name)>> resolveLookup)
    {
        if (terms.Select(t => t["Code"]).Distinct().Count() != terms.Count)
            throw new ApiFault(422, "unsupported_terms", "This terms configuration requires a reviewed extension.");
        var byCode = new Dictionary<string, Dictionary<string, string?>>();
        foreach (var input in inputs)
        {
            var code = input.GetValueOrDefault("Code");
            var definition = terms.SingleOrDefault(t => t["Code"] == code);
            if (code is null || definition is null || !byCode.TryAdd(code, input) ||
                input.Keys.Any(k => k != "Code" && k != (definition["Editor"] == "lookup" ? "ValueCode" : "Value")))
                throw new ApiFault(422, "invalid_terms", "Unknown, duplicate, or invalid term input.");
        }
        foreach (var (term, index) in terms.Select((term, index) => (term, index)))
        {
            var input = byCode.GetValueOrDefault(term["Code"]!);
            var path = $"terms[{index}].Value";
            var label = term.GetValueOrDefault("Label") ?? "this term";
            var editor = term["Editor"];
            if (term["TableName"]?.Trim() != documentTable || !string.IsNullOrWhiteSpace(term["SaveIn"]) ||
                editor == "unsupported" || editor == "lookup" && string.IsNullOrEmpty(term.GetValueOrDefault("LookupId")))
                throw ApiFault.At(422, "unsupported_terms", $"The configuration for {label} requires administrator review.", path);
            if (term.GetValueOrDefault("LookupId") is { } lookupId)
            {
                var key = input?.GetValueOrDefault("ValueCode")?.Trim();
                term["ValueCode"] = null;
                term["Value"] = null;
                if (!string.IsNullOrEmpty(key))
                {
                    var (code, name) = await resolveLookup(lookupId, key);
                    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                        throw ApiFault.At(422, "invalid_term_selection", $"Select an available value for {label} from the lookup.", path);
                    term["ValueCode"] = code;
                    term["Value"] = name;
                }
                else if (term["Mandatory"] == "Y")
                    throw ApiFault.At(422, "term_required", $"Select a value for {label} from the lookup.", path);
            }
            else term["Value"] = input?.GetValueOrDefault("Value") ?? term.GetValueOrDefault("DefaultValue");
            if (term["Mandatory"] == "Y" && string.IsNullOrWhiteSpace(term["Value"]))
                throw ApiFault.At(422, "term_required", "This term is required.", path);
            if (term["Value"]?.Length > 250)
                throw ApiFault.At(422, "term_length", "Term values must not exceed 250 characters.", path);
            if (!string.IsNullOrWhiteSpace(term["Value"]) && !ValidScalar(editor, term["Value"]!))
                throw ApiFault.At(422, "invalid_term_value", $"Enter a valid {editor} for {label}.", path);
        }
    }

    private static bool ValidScalar(string? editor, string value) => editor switch
    {
        "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "integer" => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        "decimal" => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _),
        _ => true
    };

    internal static Dictionary<string, object?> Snapshot(Dictionary<string, string?> term, string documentId,
        string voucher, string documentTable) => new()
    {
        ["Ref_DocId"] = documentId, ["TCMast_Code"] = term["Code"], ["VType"] = voucher,
        ["V_SNo"] = int.Parse(term["SNo"]!, CultureInfo.InvariantCulture), ["TCManualCode"] = term["TCManualCode"],
        ["TCFieldDataType"] = int.Parse(term["TCFieldDataType"]!, CultureInfo.InvariantCulture),
        ["Value"] = term["Value"], ["ValueCode"] = term.GetValueOrDefault("ValueCode"), ["TableName"] = documentTable
    };
}
