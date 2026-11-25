using System.Security.Claims;
using System.Security.Cryptography;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

public interface IIdentityService
{
    Task<string> AuthenticateAsync(LoginRequest request, CancellationToken ct);
    Task<SessionResponse> DescribeAsync(string username, string company, string? site, CancellationToken ct);
    Task<AuthenticationResponse> GetAuthenticationAsync(string sessionId, string username, CancellationToken ct);
    Task<CompanySelectionResponse> CompanySelectionAsync(string sessionId, string username, CancellationToken ct);
    Task<SessionResponse> SelectCompanyAsync(string sessionId, string username, SelectCompanyRequest request, CancellationToken ct);
    Task<SessionResponse> RequireContextAsync(string sessionId, string username, string? version, CancellationToken ct);
    Task<bool> IsSessionValidAsync(string sessionId, string username, CancellationToken ct);
    Task RevokeAsync(string sessionId, CancellationToken ct);
}

public sealed class IdentityService(Database database, PocOptions options, ICompanyContextService companies) : IIdentityService
{
    public async Task<string> AuthenticateAsync(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 10 ||
            string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 256)
            throw new ApiFault(401, "invalid_credentials", "Invalid username or password.");
        await using var connection = await database.OpenAsync(true, ct);
        var rows = await Database.QueryAsync(connection, null,
            "SELECT User_Name,PassWD FROM dbo.UserMast WHERE User_Name=@user", ct, ("@user", request.Username.Trim()));
        if (rows.Count != 1 || !LegacyCredentials.Verify(rows[0]["User_Name"]!, request.Password, rows[0]["PassWD"] ?? ""))
            throw new ApiFault(401, "invalid_credentials", "Invalid username or password.");
        var username = rows[0]["User_Name"]!;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await Database.ExecuteAsync(connection, null,
            "INSERT webpoc.Session(Id,Username,ExpiresAt) VALUES(@id,@user,DATEADD(hour,8,SYSUTCDATETIME()))",
            ct, ("@id", token), ("@user", username));
        return token;
    }

    public async Task<bool> IsSessionValidAsync(string sessionId, string username, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(true, ct);
        var rows = await Database.QueryAsync(connection, null,
            """
            SELECT s.Username FROM webpoc.Session s JOIN dbo.UserMast u ON u.User_Name=s.Username
            WHERE s.Id=@id AND s.Username=@user AND s.ExpiresAt>SYSUTCDATETIME()
            """, ct, ("@id", sessionId), ("@user", username));
        return rows.Count == 1;
    }

    public async Task RevokeAsync(string sessionId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(true, ct);
        await Database.ExecuteAsync(connection, null, "DELETE webpoc.Session WHERE Id=@id", ct, ("@id", sessionId));
    }

    private async Task<Dictionary<string, string?>> SessionAsync(string sessionId, string username, CancellationToken ct)
    {
        await using var control = await database.OpenAsync(true, ct);
        var rows = await Database.QueryAsync(control, null,
            """
            SELECT s.Username,s.Company,s.Site,s.ContextVersion FROM webpoc.Session s
            JOIN dbo.UserMast u ON u.User_Name=s.Username
            WHERE s.Id=@id AND s.Username=@user AND s.ExpiresAt>SYSUTCDATETIME()
            """, ct, ("@id", sessionId), ("@user", username));
        return rows.SingleOrDefault() ?? throw new ApiFault(401, "session_expired", "Sign in again to continue.");
    }

    public async Task<AuthenticationResponse> GetAuthenticationAsync(string sessionId, string username, CancellationToken ct)
    {
        var row = await SessionAsync(sessionId, username, ct);
        if (row["Company"] is null && row["Site"] is null && row["ContextVersion"] is null)
            return new(row["Username"]!, null);
        if (row["Company"] is null || row["Site"] is null || row["ContextVersion"] is null)
            throw new ApiFault(409, "context_required", "Choose a company and financial year to continue.");
        var selected = await DescribeAsync(row["Username"]!, row["Company"]!, row["Site"]!, ct);
        return new(row["Username"]!, selected with { ContextVersion = row["ContextVersion"]! });
    }

    public async Task<CompanySelectionResponse> CompanySelectionAsync(string sessionId, string username, CancellationToken ct)
    {
        var row = await SessionAsync(sessionId, username, ct);
        return new(row["Username"]!, row["Company"], row["Site"], row["ContextVersion"], await companies.OptionsAsync(username, ct));
    }

    public async Task<SessionResponse> SelectCompanyAsync(string sessionId, string username, SelectCompanyRequest request, CancellationToken ct)
    {
        var selected = await DescribeAsync(username, request.Company, request.Site, ct);
        var version = Guid.NewGuid().ToString("N");
        await using var control = await database.OpenAsync(true, ct);
        var changed = await Database.ExecuteAsync(control, null,
            """
            UPDATE webpoc.Session SET Company=@company,Site=@site,ContextVersion=@version
            WHERE Id=@id AND Username=@user AND ExpiresAt>SYSUTCDATETIME()
              AND (ContextVersion=@expected OR (ContextVersion IS NULL AND @expected IS NULL))
            """, ct, ("@company", selected.Company), ("@site", selected.Site), ("@version", version),
            ("@id", sessionId), ("@user", username), ("@expected", request.ExpectedVersion));
        if (changed != 1)
            throw new ApiFault(409, "context_changed", "The session or company selection changed. Reload the company selection page and retry.");
        return selected with { ContextVersion = version };
    }

    public async Task<SessionResponse> RequireContextAsync(string sessionId, string username, string? version, CancellationToken ct)
    {
        var authentication = await GetAuthenticationAsync(sessionId, username, ct);
        var selected = authentication.Context
            ?? throw new ApiFault(409, "context_required", "Choose a company and financial year before opening ERP modules.");
        if (string.IsNullOrEmpty(version) || !string.Equals(version, selected.ContextVersion, StringComparison.Ordinal))
            throw new ApiFault(409, "context_changed",
                "The company selection changed or this page is out of date. Preserve any unsaved details, then reopen the company selection page.");
        return selected;
    }

    public async Task<SessionResponse> DescribeAsync(string username, string companyCode, string? siteCode, CancellationToken ct)
    {
        var selected = await companies.ResolveAsync(username, companyCode, siteCode, ct);
        await using var control = await database.OpenAsync(true, ct);
        var grants = await Database.QueryAsync(control, null,
            """
            SELECT Permission,ISNULL(Active,'Y') AS Active FROM dbo.User_Permission
            WHERE UserName=@user AND MnuModule='Sales' AND MnuName='MnuSaleOrder' AND Comp_Code IN (@company,'')
            """, ct, ("@user", username), ("@company", selected.Company.Company));
        var grant = SalesRules.ResolveGrant(grants.Select(r => r["Active"] == "Y" ? r["Permission"] ?? "" : "!inactive"));
        var view = grant is not null and not "!inactive";
        var groups = await Database.QueryAsync(control, null,
            """
            SELECT GroupText FROM dbo.User_Control_Permission
            WHERE UserName=@user AND MnuModule='Sales' AND MnuName='MnuSaleOrder' AND Comp_Code IN (@company,'')
            """, ct, ("@user", username), ("@company", selected.Company.Company));
        var restricted = await Database.QueryAsync(control, null,
            "SELECT MnuName FROM dbo.Form_Restrict_System WHERE MnuModule='Sales' AND MnuName='MnuSaleOrder'", ct);
        if (restricted.Count > 0) view = false;
        return new SessionResponse(username, selected.Company.Company, selected.Company.CompanyName,
            selected.Site.Code, selected.Company.FinancialYear, selected.Prefix, selected.Company.StartDate,
            selected.Company.EndDate, new CapabilitiesResponse(view,
                view && !selected.ReadOnly && grant!.Contains('A'), view && !selected.ReadOnly && grant!.Contains('E'), view && !selected.ReadOnly && grant!.Contains('D'),
                view && grant!.Contains('P'), view && groups.Any(g => g["GroupText"] == "Selected Price List"),
                view && !selected.ReadOnly && username.Equals(options.MetadataAdministrator, StringComparison.OrdinalIgnoreCase),
                view && !selected.ReadOnly && groups.Any(g => g["GroupText"]?.Trim() == "Approved By")),
            selected.Site.Name, ReadOnly: selected.ReadOnly, CompanyInformation: selected.Information);
    }

}

public static class IdentityContextExtensions
{
    public static Task<SessionResponse> CurrentAsync(this IIdentityService identity, HttpContext context, CancellationToken ct) =>
        identity.RequireContextAsync(context.User.FindFirstValue("sid")!, context.User.Identity!.Name!,
            context.Request.Headers["X-ERP-Context"].FirstOrDefault(), ct);
}
