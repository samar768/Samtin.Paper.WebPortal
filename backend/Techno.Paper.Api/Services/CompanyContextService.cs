using System.Globalization;
using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

public sealed record ResolvedCompanyContext(CompanyOptionResponse Company, SiteOptionResponse Site,
    string Prefix, CompanyInformationResponse Information, bool ReadOnly);

public interface ICompanyContextService
{
    Task<IReadOnlyList<CompanyOptionResponse>> OptionsAsync(string username, CancellationToken ct);
    Task<ResolvedCompanyContext> ResolveAsync(string username, string company, string? site, CancellationToken ct);
}

public sealed class CompanyContextService(Database database, PocOptions options) : ICompanyContextService
{
    private static string Text(Dictionary<string, string?> row, string key) => row.GetValueOrDefault(key)?.Trim() ?? "";
    private static string Effective(Dictionary<string, string?> site, string siteKey, Dictionary<string, string?> company, string companyKey) =>
        Text(site, siteKey) is { Length: > 0 } value ? value : Text(company, companyKey);

    private async Task<(IReadOnlyList<CompanyOptionResponse> Options, List<Dictionary<string, string?>> Companies,
        List<Dictionary<string, string?>> Sites)> LoadAsync(string username, string? selected, CancellationToken ct)
    {
        await using var control = await database.OpenAsync(true, ct);
        var companies = await Database.QueryAsync(control, null,
            """
            SELECT c.Comp_Code,c.Comp_Name,c.CYear,c.V_Prefix,
              CONVERT(varchar(10),c.Start_Dt,23) AS StartDate,CONVERT(varchar(10),c.End_Dt,23) AS EndDate,
              c.CentralData_Path,c.Address1,c.Address2,c.City,c.Country,c.Pin,c.Phone,c.Email,c.PanNo,c.TinNo
            FROM dbo.Company c WHERE (@company IS NULL OR c.Comp_Code=@company)
              AND EXISTS(SELECT 1 FROM dbo.UserMast WHERE User_Name=@user)
              AND (@super=1 OR EXISTS(SELECT 1 FROM dbo.UserSite s WHERE s.User_Name=@user AND s.CompCode=c.Comp_Code))
            ORDER BY c.Start_Dt DESC,c.Comp_Name,c.Comp_Code
            """, ct, ("@company", selected), ("@user", username), ("@super", CompanyContextRules.IsSuperuser(username)));
        if (companies.Count == 0) return ([], companies, []);
        var grants = await Database.QueryAsync(control, null,
            "SELECT CompCode,SiteList FROM dbo.UserSite WHERE User_Name=@user", ct, ("@user", username));
        await using var transaction = await database.OpenAsync(false, ct);
        var sites = await Database.QueryAsync(transaction, null,
            """
            SELECT Code,Name,HO_YN,SiteDesc,Address1,Address2,City,Pin,PANNo,TinNo
            FROM dbo.SiteMast ORDER BY Name,Code
            """, ct);
        var configuredDatabase = new SqlConnectionStringBuilder(options.TransactionConnection).InitialCatalog;
        var result = new List<CompanyOptionResponse>();
        foreach (var company in companies)
        {
            var code = Text(company, "Comp_Code");
            var supported = string.Equals(Text(company, "CentralData_Path"), configuredDatabase, StringComparison.OrdinalIgnoreCase);
            var siteGrants = grants.Where(grant => Text(grant, "CompCode") == code).Select(grant => Text(grant, "SiteList"))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (siteGrants.Length > 1 && !CompanyContextRules.IsSuperuser(username))
                throw new ApiFault(409, "site_configuration", "Conflicting site grants require administrator review.");
            var permitted = CompanyContextRules.IsSuperuser(username)
                ? sites.Select(site => Text(site, "Code")).ToArray()
                : CompanyContextRules.ParseSites(siteGrants.SingleOrDefault() ?? "");
            var allowedSites = supported ? sites.Where(site => permitted.Contains(Text(site, "Code"), StringComparer.OrdinalIgnoreCase))
                .Select(site => new SiteOptionResponse(Text(site, "Code"), Text(site, "Name"), Text(site, "HO_YN") == "Y")).ToArray() : [];
            var start = Text(company, "StartDate");
            var end = Text(company, "EndDate");
            if (!DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) ||
                !DateOnly.TryParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last) ||
                last < first || string.IsNullOrEmpty(Text(company, "V_Prefix")) || string.IsNullOrEmpty(Text(company, "CYear")))
                throw new ApiFault(409, "company_configuration", "A company has invalid financial-year configuration. Request administrator review.");
            var reason = !supported ? "This company's database is not configured for the web POC."
                : allowedSites.Length == 0 ? "No authorized site is available for this company." : null;
            result.Add(new(code, Text(company, "Comp_Name"), Text(company, "CYear"), start, end, allowedSites,
                reason is null, reason, code != options.Company));
        }
        return (result, companies, sites);
    }

    public async Task<IReadOnlyList<CompanyOptionResponse>> OptionsAsync(string username, CancellationToken ct) =>
        (await LoadAsync(username, null, ct)).Options;

    public async Task<ResolvedCompanyContext> ResolveAsync(string username, string company, string? site, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(company) || company.Length > 2 || site?.Length > 2)
            throw new ApiFault(403, "context_denied", "Choose an authorized company and site.");
        var loaded = await LoadAsync(username, company, ct);
        var selected = loaded.Options.SingleOrDefault()
            ?? throw new ApiFault(403, "context_denied", "This company is not permitted.");
        if (!selected.Available)
            throw new ApiFault(409, "context_unavailable", selected.UnavailableReason!);
        var selectedSite = CompanyContextRules.SelectSite(selected.Sites, site);
        var companyRow = loaded.Companies.Single();
        var siteRow = loaded.Sites.Single(row => Text(row, "Code") == selectedSite.Code);
        var information = new CompanyInformationResponse(selected.CompanyName,
            Effective(siteRow, "SiteDesc", companyRow, "Comp_Name"),
            Effective(siteRow, "Address1", companyRow, "Address1"), Effective(siteRow, "Address2", companyRow, "Address2"),
            Effective(siteRow, "City", companyRow, "City"), Text(companyRow, "Country"),
            Effective(siteRow, "Pin", companyRow, "Pin"), Text(companyRow, "Phone"), Text(companyRow, "Email"),
            Effective(siteRow, "PANNo", companyRow, "PanNo"), Effective(siteRow, "TinNo", companyRow, "TinNo"), selectedSite.HeadOffice);
        return new(selected, selectedSite, Text(companyRow, "V_Prefix"), information,
            selected.ReadOnly || selectedSite.Code != options.Site);
    }
}
