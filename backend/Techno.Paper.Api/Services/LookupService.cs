using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

public interface ILookupAccessPolicy
{
    string Resource { get; }
    Task<bool> CanViewAsync(SessionResponse session, CancellationToken ct);
}

public sealed class SalesLookupAccessPolicy : ILookupAccessPolicy
{
    public string Resource => "sales-orders";
    public Task<bool> CanViewAsync(SessionResponse session, CancellationToken ct) => Task.FromResult(session.Capabilities.View);
}

public sealed class EnvironmentLookupAccessPolicy(Database database, PocOptions options) : ILookupAccessPolicy
{
    public string Resource => "environment-settings";
    public async Task<bool> CanViewAsync(SessionResponse session, CancellationToken ct) =>
        (await MasterAccess.ReadAsync(database, options, session, "Master", "MnuEnvironmentSetting", ct)).View;
}

public interface ILookupService
{
    Task<LookupDefinitionResponse> DefinitionAsync(SessionResponse session, string id, CancellationToken ct);
    Task<LookupSearchResponse> SearchAsync(SessionResponse session, string id, string? search, int page, CancellationToken ct);
    Task<LookupResolveResponse> ResolveAsync(SessionResponse session, string id, ResolveLookupRequest request, CancellationToken ct);
}

public sealed class LookupService(Database database, IEnumerable<ILookupAccessPolicy> policies, IMasterService masters) : ILookupService
{
    private sealed record Column(string Key, string Caption, int Position, int Width, bool Visible, bool Searchable, int? LabelOrder);
    private sealed record Source(LookupDefinitionResponse Definition, string View, string Key, string KeyType, int KeyLength,
        string ScopeMode, string? ScopeColumn, string Separator, List<Column> Columns, TermLookupSource? TermSource = null);

    private static ApiFault ConfigurationError() => new(409, "lookup_configuration",
        "This lookup configuration needs an administrator review. No reference value has been changed.");

    private static string Identifier(string name)
    {
        if (name.StartsWith("__", StringComparison.Ordinal) || !Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
            throw ConfigurationError();
        return $"[{name}]";
    }

    private async Task<Source> LoadAsync(SqlConnection connection, SessionResponse session, string id, CancellationToken ct)
    {
        if (!Regex.IsMatch(id, @"^[a-zA-Z0-9_-]{1,60}$"))
            throw new ApiFault(400, "invalid_lookup", "Invalid lookup identifier.");
        if (id.StartsWith(VoucherTerms.LookupPrefix, StringComparison.Ordinal))
        {
            var separator = id.LastIndexOf('-');
            if (separator <= VoucherTerms.LookupPrefix.Length || separator == id.Length - 1)
                throw new ApiFault(404, "lookup_not_found", "This lookup is not available.");
            var voucher = id[VoucherTerms.LookupPrefix.Length..separator];
            var access = policies.SingleOrDefault(policy => policy.Resource == "sales-orders");
            if (access is null) throw ConfigurationError();
            if (!await access.CanViewAsync(session, ct)) throw new ApiFault(403, "lookup_denied", "This lookup is not permitted.");
            var bindings = new Dictionary<string, TermLookupSource>();
            var terms = await VoucherTerms.LoadAsync(connection, null, voucher, ct, bindings, access.Resource, "SORD");
            if (!bindings.TryGetValue(id, out var source))
                throw new ApiFault(404, "lookup_not_found", "This lookup is not available.");
            if (source.KeyLength is < 1 or > 50 || source.Key == source.Label ||
                source.KeyType is not ("char" or "varchar" or "nchar" or "nvarchar")) throw ConfigurationError();
            var term = terms.Single(row => row.GetValueOrDefault("LookupId") == id);
            var format = await Database.QueryAsync(connection, null,
                """
                SELECT ColIndex,HeaderText,ColWidth,ColDisplay FROM dbo.Terms_Detail_ColFormat
                WHERE VType=@voucher AND TCFieldCode=@code ORDER BY ColIndex
                """, ct, ("@voucher", voucher), ("@code", term["Code"]));
            Column MakeColumn(int position, string key, string caption, int width, int? labelOrder)
            {
                var overrideRow = format.FirstOrDefault(row => row["ColIndex"] == position.ToString(CultureInfo.InvariantCulture));
                return new(key, string.IsNullOrWhiteSpace(overrideRow?.GetValueOrDefault("HeaderText")) ? caption : overrideRow["HeaderText"]!,
                    position, int.TryParse(overrideRow?.GetValueOrDefault("ColWidth"), out var configuredWidth) && configuredWidth > 0
                        ? Math.Clamp(configuredWidth, 40, 400) : width,
                    overrideRow is null || overrideRow["ColDisplay"]?.Trim() == "Y", true, labelOrder);
            }
            List<Column> termColumns = [MakeColumn(0, source.Key, source.Key, 105, null), MakeColumn(1, source.Label, source.Label, 280, 1)];
            if (!termColumns.Any(column => column.Visible)) throw ConfigurationError();
            var termVersion = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { term, source, termColumns })));
            return new(new(id, term["Label"] ?? "Select term", termVersion, 25,
                termColumns.Where(column => column.Visible).Select(column => new LookupColumnResponse(column.Key, column.Caption, column.Width)).ToList(), null),
                source.Rows, source.Key, source.KeyType, source.KeyLength, "none", null, "", termColumns, source);
        }
        var definitions = await Database.QueryAsync(connection, null,
            "SELECT * FROM webpoc.LookupDefinition WHERE Id=@id AND Enabled=1", ct, ("@id", id));
        if (definitions.Count != 1) throw new ApiFault(404, "lookup_not_found", "This lookup is not available.");
        var definition = definitions[0];
        var policy = policies.SingleOrDefault(policy => policy.Resource == definition["Resource"]);
        if (policy is null) throw ConfigurationError();
        if (!await policy.CanViewAsync(session, ct)) throw new ApiFault(403, "lookup_denied", "This lookup is not permitted.");
        var configured = await Database.QueryAsync(connection, null,
            "SELECT * FROM webpoc.LookupColumn WHERE LookupId=@id ORDER BY Position", ct, ("@id", id));
        var shape = await Database.QueryAsync(connection, null,
            """
            SELECT c.name AS ColumnName,t.name AS SqlType,c.max_length AS Length
            FROM sys.views v JOIN sys.schemas s ON s.schema_id=v.schema_id JOIN sys.columns c ON c.object_id=v.object_id
            JOIN sys.types t ON t.user_type_id=c.user_type_id
            WHERE s.name='webpoc' AND v.name=@view ORDER BY c.column_id
            """, ct, ("@view", definition["SourceView"]));
        var key = definition["KeyColumn"]!;
        var keyShape = shape.SingleOrDefault(column => column["ColumnName"] == key);
        var scope = definition["ScopeMode"]!;
        var scopeColumn = definition["ScopeColumn"];
        if (keyShape is null || keyShape["SqlType"] is not ("char" or "varchar" or "nchar" or "nvarchar") ||
            int.Parse(keyShape["Length"]!, CultureInfo.InvariantCulture) is < 1 or > 400 ||
            configured.Count is < 1 or > 16 || !configured.Any(column => column["Visible"] == "True") ||
            !configured.Any(column => column["Searchable"] == "True") ||
            !configured.Any(column => column["LabelOrder"] is not null) ||
            configured.Any(column => !shape.Any(source => source["ColumnName"] == column["ColumnName"])) ||
            scope is not ("list" or "code" or "none") ||
            scope != "none" && !shape.Any(column => column["ColumnName"] == scopeColumn))
            throw ConfigurationError();
        var columns = configured.Select(column => new Column(column["ColumnName"]!, column["Caption"]!,
            int.Parse(column["Position"]!, CultureInfo.InvariantCulture), int.Parse(column["Width"]!, CultureInfo.InvariantCulture),
            column["Visible"] == "True", column["Searchable"] == "True",
            column["LabelOrder"] is { } order ? int.Parse(order, CultureInfo.InvariantCulture) : null)).ToList();
        var pageSize = int.Parse(definition["PageSize"]!, CultureInfo.InvariantCulture);
        if (pageSize is < 10 or > 50 || columns.Any(column => column.Width is < 40 or > 400) ||
            columns.Where(column => column.LabelOrder is not null).Select(column => column.LabelOrder).Distinct().Count() !=
                columns.Count(column => column.LabelOrder is not null)) throw ConfigurationError();
        var view = $"webpoc.{Identifier(definition["SourceView"]!)}";
        _ = Identifier(key);
        foreach (var column in columns) _ = Identifier(column.Key);
        if (scopeColumn is not null) _ = Identifier(scopeColumn);
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { definition, configured, shape }))));
        var response = new LookupDefinitionResponse(id, definition["Title"]!, version, pageSize,
            columns.Where(column => column.Visible).Select(column => new LookupColumnResponse(column.Key, column.Caption, column.Width)).ToList(),
            await masters.LookupCreateAsync(session, id, ct));
        var keyType = keyShape["SqlType"]!;
        var keyLength = int.Parse(keyShape["Length"]!, CultureInfo.InvariantCulture) / (keyType.StartsWith('n') ? 2 : 1);
        return new(response, view, key, keyType, keyLength, scope, scopeColumn, definition["LabelSeparator"]!, columns);
    }

    private static string Scope(Source source) => source.ScopeMode switch
    {
        "list" => $"(NULLIF(s.{Identifier(source.ScopeColumn!)},'') IS NULL OR CHARINDEX('|' + @site + '|','|' + s.{Identifier(source.ScopeColumn!)} + '|')>0)",
        "code" => $"s.{Identifier(source.ScopeColumn!)}=@site",
        _ => "1=1"
    };

    private static string Projection(Source source) => string.Join(",",
        source.Columns.Select(column => column.Key).Append(source.Key).Distinct().Select(key => $"s.{Identifier(key)}"));

    private static LookupOptionResponse Option(Source source, Dictionary<string, string?> row, string? requestedKey = null)
    {
        var key = requestedKey ?? row[source.Key];
        var label = string.Join(source.Separator, source.Columns.Where(column => column.LabelOrder is not null)
            .OrderBy(column => column.LabelOrder).Select(column => row[column.Key]?.Trim()).Where(value => !string.IsNullOrEmpty(value)));
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(label)) throw ConfigurationError();
        return new(key, label, source.Columns.Where(column => column.Visible).ToDictionary(column => column.Key, column => row[column.Key]));
    }

    public async Task<LookupDefinitionResponse> DefinitionAsync(SessionResponse session, string id, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(false, ct);
        return (await LoadAsync(connection, session, id, ct)).Definition;
    }

    public async Task<LookupSearchResponse> SearchAsync(SessionResponse session, string id, string? search, int page, CancellationToken ct)
    {
        if (search?.Length > 100 || page is < 0 or > 10000)
            throw new ApiFault(400, "invalid_lookup_search", "Invalid lookup search or page.");
        await using var connection = await database.OpenAsync(false, ct);
        var source = await LoadAsync(connection, session, id, ct);
        var filter = string.Join(" OR ", source.Columns.Where(column => column.Searchable)
            .Select(column => $"CHARINDEX(@search,CONVERT(nvarchar(4000),s.{Identifier(column.Key)}))>0"));
        var sort = source.TermSource?.Order ?? string.Join(",", source.Columns.Where(column => column.LabelOrder is not null)
            .OrderBy(column => column.LabelOrder).Select(column => $"s.{Identifier(column.Key)}").Append($"s.{Identifier(source.Key)}").Distinct());
        var rows = await Database.QueryAsync(connection, null,
            $"""
            WITH Matches AS (
              SELECT {Projection(source)},ROW_NUMBER() OVER(ORDER BY {sort}) AS __LookupRow
              FROM {source.View} s WHERE {Scope(source)} AND (@search='' OR {filter}))
            SELECT * FROM Matches WHERE __LookupRow>@skip AND __LookupRow<=@skip+@take ORDER BY __LookupRow
            """, ct, ("@site", session.Site), ("@search", search?.Trim() ?? ""),
            ("@skip", page * source.Definition.PageSize), ("@take", source.Definition.PageSize + 1), ("@termLimit", source.TermSource?.Limit));
        var options = rows.Take(source.Definition.PageSize).Select(row => Option(source, row)).ToList();
        if (options.Select(option => option.Key).Distinct().Count() != options.Count) throw ConfigurationError();
        return new(source.Definition, options, page, rows.Count > source.Definition.PageSize);
    }

    public async Task<LookupResolveResponse> ResolveAsync(SessionResponse session, string id, ResolveLookupRequest request, CancellationToken ct)
    {
        if (request.Keys.Count is < 1 or > 100 || request.Keys.Any(key => string.IsNullOrWhiteSpace(key)))
            throw new ApiFault(400, "invalid_lookup_keys", "Resolve requires 1-100 nonempty reference keys.");
        await using var connection = await database.OpenAsync(false, ct);
        var source = await LoadAsync(connection, session, id, ct);
        if (request.Keys.Any(key => key.Length > source.KeyLength))
            throw new ApiFault(400, "invalid_lookup_keys", "A reference key exceeds the configured source length.");
        var keys = request.Keys.Distinct().ToArray();
        var parameters = keys.Select((key, index) => ($"@key{index}", (object?)key)).Append(("@site", session.Site))
            .Append(("@termLimit", source.TermSource?.Limit)).ToArray();
        var values = string.Join(",", keys.Select((_, index) =>
            $"(@key{index},CAST(@key{index} AS {source.KeyType}({source.KeyLength})))"));
        var rows = await Database.QueryAsync(connection, null,
            $"""
            SELECT {Projection(source)},requested.OriginalKey AS __RequestedKey
            FROM {source.View} s JOIN (VALUES {values}) requested(OriginalKey,MatchKey)
              ON s.{Identifier(source.Key)}=requested.MatchKey
            WHERE {Scope(source)}
            """, ct, parameters);
        var options = rows.Select(row => Option(source, row, row["__RequestedKey"])).ToList();
        if (options.Select(option => option.Key).Distinct().Count() != options.Count) throw ConfigurationError();
        return new(source.Definition, options, keys.Except(options.Select(option => option.Key)).ToList());
    }
}
