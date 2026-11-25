using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Domain;

public static class CompanyContextRules
{
    public static bool IsSuperuser(string username) =>
        username.Equals("SA", StringComparison.OrdinalIgnoreCase) || username.Equals("SUPER", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ParseSites(string value)
    {
        var codes = value.Split(['|', ',', ';', '\'', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Any(code => code.Length > 2))
            throw new ApiFault(409, "site_configuration", "A user site grant contains an invalid site code. Request administrator review.");
        return codes;
    }

    public static SiteOptionResponse SelectSite(IReadOnlyList<SiteOptionResponse> sites, string? requested)
    {
        if (sites.Count == 1 && string.IsNullOrEmpty(requested)) return sites[0];
        if (string.IsNullOrEmpty(requested) && sites.Count > 1)
            throw ApiFault.At(422, "site_required", "Choose an authorized site.", "site");
        return sites.SingleOrDefault(site => site.Code.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? throw new ApiFault(403, "context_denied", "This site is not permitted for the selected company.");
    }
}
