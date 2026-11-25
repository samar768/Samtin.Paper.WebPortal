using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Infrastructure;

namespace Techno.Paper.Api.Services;

internal sealed record TermLookupSource(string Resource, string Relation, string Key, string Label,
    string KeyType, int KeyLength, int? Limit, bool SortByLabel, bool Descending)
{
    internal string Order => SortByLabel
        ? $"[{Label}]{(Descending ? " DESC" : "")},[{Key}]"
        : $"[{Key}]{(Descending ? " DESC" : "")}";

    internal string Rows => Limit is null ? Relation :
        $"(SELECT TOP (@termLimit) [{Key}],[{Label}] FROM {Relation} ORDER BY {Order})";
}

internal static class VoucherTermLookup
{
    private const string Identifier = @"(?:[A-Za-z_][A-Za-z0-9_]{0,127}|\[[A-Za-z_][A-Za-z0-9_]{0,127}\])";
    private static readonly Regex PlainIdentifier = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Select = new(
        $@"\A\s*SELECT\s+(?:TOP\s*(?:(?<limit>[0-9]{{1,5}})|\(\s*(?<limit>[0-9]{{1,5}})\s*\))\s+)?(?<key>{Identifier})\s*,\s*(?<label>{Identifier})\s+FROM\s+(?:(?<schema>{Identifier})\s*\.\s*)?(?<table>{Identifier})(?:\s+ORDER\s+BY\s+(?<sort>{Identifier})(?:\s+(?<direction>ASC|DESC))?)?\s*;?\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal sealed record Definition(string Schema, string Table, string Key, string Label, int? Limit, bool SortByLabel, bool Descending);

    internal static Definition? Parse(string? query)
    {
        if (query is not { Length: > 0 and <= 1000 }) return null;
        var match = Select.Match(query);
        if (!match.Success) return null;
        string Token(string name) => match.Groups[name].Value.Trim('[', ']');
        var key = Token("key");
        var label = Token("label");
        var sort = Token("sort");
        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        if (Same(key, label) || key.StartsWith("__", StringComparison.Ordinal) || label.StartsWith("__", StringComparison.Ordinal) ||
            sort.Length > 0 && !Same(sort, key) && !Same(sort, label)) return null;
        int? limit = match.Groups["limit"].Success ? int.Parse(Token("limit"), CultureInfo.InvariantCulture) : null;
        if (limit is < 1 or > 10000) return null;
        return new(match.Groups["schema"].Success ? Token("schema") : "dbo", Token("table"), key, label,
            limit, Same(sort, label), Same(Token("direction"), "DESC"));
    }

    internal static async Task<TermLookupSource?> BindAsync(SqlConnection connection, SqlTransaction? transaction,
        Definition definition, string resource, CancellationToken ct)
    {
        var columns = await Database.QueryAsync(connection, transaction,
            """
            SELECT s.name AS SchemaName,o.name AS ObjectName,c.name,TYPE_NAME(c.system_type_id) AS SqlType,c.max_length AS ByteLength,
                CASE WHEN c.name=@key THEN 'key' ELSE 'label' END AS Slot
            FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
            JOIN sys.columns c ON c.object_id=o.object_id
            WHERE s.name=@schema AND o.name=@table AND o.type IN ('U','V') AND o.is_ms_shipped=0
              AND c.name IN (@key,@label) AND TYPE_NAME(c.system_type_id) IN ('char','varchar','nchar','nvarchar')
              AND EXISTS (
                  SELECT 1 FROM sys.database_permissions p JOIN sys.database_principals r ON r.principal_id=p.grantee_principal_id
                  WHERE r.type='R' AND r.name=@role AND p.class=1 AND p.major_id=o.object_id
                    AND p.minor_id=c.column_id AND p.permission_name='SELECT' AND p.state='G')
            """, ct, ("@schema", definition.Schema), ("@table", definition.Table), ("@key", definition.Key),
            ("@label", definition.Label), ("@role", $"webpoc-term-{resource}"));
        if (columns.Count != 2 || columns.Count(c => c["Slot"] == "key") != 1) return null;
        var key = columns.Single(c => c["Slot"] == "key");
        var label = columns.Single(c => c["Slot"] == "label");
        var schemaName = key["SchemaName"]!;
        var objectName = key["ObjectName"]!;
        var keyName = key["name"]!;
        var labelName = label["name"]!;
        if (new[] { schemaName, objectName, keyName, labelName }.Any(name => !PlainIdentifier.IsMatch(name)) ||
            keyName.StartsWith("__", StringComparison.Ordinal) || labelName.StartsWith("__", StringComparison.Ordinal))
            return null;
        var keyType = key["SqlType"]!;
        var keyLength = int.Parse(key["ByteLength"]!, CultureInfo.InvariantCulture) / (keyType.StartsWith('n') ? 2 : 1);
        if (keyLength is < 1 or > 50) return null;
        return new(resource, $"[{schemaName}].[{objectName}]", keyName, labelName, keyType,
            keyLength, definition.Limit, definition.SortByLabel, definition.Descending);
    }
}
