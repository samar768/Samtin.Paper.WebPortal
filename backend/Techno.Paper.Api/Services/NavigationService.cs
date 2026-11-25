using System.Globalization;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

public sealed record MenuGrant(string Module, string Name, string Label, string Parent, string ReportFor,
    int Order, string Permission, bool Active);
public sealed record MenuAccess(IReadOnlyList<MenuGrant> Grants, IReadOnlySet<string> Restricted);

public static class NavigationRules
{
    public static NavigationResponse Build(IEnumerable<MenuGrant> source, IReadOnlySet<string> restricted,
        IReadOnlyDictionary<string, string>? masterRoutes = null)
    {
        var modules = new List<NavigationModuleResponse>();
        var warnings = new HashSet<string>();
        foreach (var placement in source.GroupBy(row => row.ReportFor, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Min(row => row.Order)))
        {
            if (string.IsNullOrWhiteSpace(placement.Key))
                throw new ApiFault(409, "navigation_configuration", "A menu has no desktop module placement. Request administrator review.");
            var nodes = placement.GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(
                group => group.Key, group =>
                {
                    if (group.Select(row => (row.Module, row.Label, row.Parent)).Distinct().Count() != 1)
                        throw new ApiFault(409, "navigation_configuration", "Conflicting menu definitions require administrator review.");
                    _ = SalesRules.ResolveGrant(group.Select(row => row.Active ? row.Permission : "!inactive"));
                    return group.First();
                }, StringComparer.OrdinalIgnoreCase);
            bool Visible(MenuGrant row)
            {
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    if (!visited.Add(row.Name))
                        throw new ApiFault(409, "navigation_configuration", "A menu hierarchy contains a cycle. Request administrator review.");
                    if (!row.Active || restricted.Contains($"{row.Module}:{row.Name}")) return false;
                    if (string.IsNullOrEmpty(row.Parent)) return true;
                    if (!nodes.TryGetValue(row.Parent, out var parent))
                    {
                        warnings.Add("Some desktop menus are hidden because their parent is not available in the current permissions. Request an ERP administrator configuration review.");
                        return false;
                    }
                    row = parent;
                }
            }
            var items = nodes.Values.Where(Visible).OrderBy(row => row.Order).Select(row =>
            {
                var key = $"{row.Module}:{row.Name}";
                var sales = row.Module.Equals("Sales", StringComparison.OrdinalIgnoreCase) &&
                    row.Name.Equals("MnuSaleOrder", StringComparison.OrdinalIgnoreCase);
                var master = masterRoutes?.GetValueOrDefault(key);
                var implemented = sales || master is not null;
                var href = sales ? "/dashboard/sale-order" : master is not null ? $"/dashboard/masters/{Uri.EscapeDataString(master)}" :
                    $"/dashboard/modules/{Uri.EscapeDataString(placement.Key)}?menu={Uri.EscapeDataString(key)}";
                return new NavigationItemResponse(key, row.Label.Replace("&", "").Trim(),
                    string.IsNullOrEmpty(row.Parent) ? null : $"{nodes[row.Parent].Module}:{nodes[row.Parent].Name}",
                    row.Order, href, implemented);
            }).ToArray();
            if (items.Length == 0) continue;
            var label = placement.Key.ToUpperInvariant() switch
            {
                "FORMMODULE" => "Forms",
                "WEBSERVICE" => "Web Service",
                _ => placement.Key
            };
            modules.Add(new NavigationModuleResponse(placement.Key, label, items));
        }
        return new NavigationResponse(modules, warnings.ToArray());
    }
}

public sealed class NavigationService(Database database)
{
    public async Task<NavigationResponse> GetAsync(SessionResponse session, CancellationToken ct)
    {
        var access = await ReadAccessAsync(database, session, ct);
        await using var connection = await database.OpenAsync(false, ct);
        var bindings = await Database.QueryAsync(connection, null,
            """
            SELECT d.MasterId,d.MenuModule,d.MenuName FROM webpoc.MasterRegistration r
            JOIN webpoc.MasterDefinition d ON d.MasterId=r.Id AND d.Revision=r.ActiveRevision WHERE r.Enabled=1
            """, ct);
        if (bindings.Select(row => $"{row["MenuModule"]}:{row["MenuName"]}").Distinct(StringComparer.OrdinalIgnoreCase).Count() != bindings.Count)
            throw new ApiFault(409, "master_configuration", "Conflicting master menu bindings require administrator review.");
        return NavigationRules.Build(access.Grants, access.Restricted,
            bindings.ToDictionary(row => $"{row["MenuModule"]}:{row["MenuName"]}", row => row["MasterId"]!,
                StringComparer.OrdinalIgnoreCase));
    }

    public static async Task<MenuAccess> ReadAccessAsync(Database database, SessionResponse session, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(true, ct);
        var rows = await Database.QueryAsync(connection, null,
            """
            SELECT MnuModule,MnuName,MnuText,ISNULL(Parent,'') AS Parent,ReportFor,
              ISNULL(SNo,0) AS SNo,ISNULL(Permission,'') AS Permission,ISNULL(Active,'Y') AS Active
            FROM dbo.User_Permission WHERE UserName=@user AND Comp_Code IN (@company,'')
            ORDER BY SNo
            """, ct, ("@user", session.Username), ("@company", session.Company));
        var restricted = await Database.QueryAsync(connection, null,
            "SELECT MnuModule,MnuName FROM dbo.Form_Restrict_System", ct);
        return new(rows.Select(row => new MenuGrant(
            row["MnuModule"]!.Trim(), row["MnuName"]!.Trim(), row["MnuText"] ?? row["MnuName"]!,
            row["Parent"]!.Trim(), row["ReportFor"]?.Trim() ?? "",
            int.Parse(row["SNo"]!, CultureInfo.InvariantCulture), row["Permission"]!, row["Active"] == "Y")).ToArray(),
            restricted.Select(row => $"{row["MnuModule"]!.Trim()}:{row["MnuName"]!.Trim()}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}
