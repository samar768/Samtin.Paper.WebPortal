using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSession(this WebApplication app)
    {
        app.MapGet("/api/csrf", (HttpContext context, IAntiforgery antiforgery, CancellationToken ct) =>
            TypedResults.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(context).RequestToken!)))
            .WithSummary("Issue a CSRF request token").WithDescription("Call before login and all state-changing requests.");
        app.MapPost("/api/session", async (LoginRequest request, HttpContext context, IIdentityService identity, CancellationToken ct) =>
        {
            var id = await identity.AuthenticateAsync(request, ct);
            var username = request.Username.Trim().ToUpperInvariant();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, username), new Claim("sid", id)], CookieAuthenticationDefaults.AuthenticationScheme));
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return TypedResults.Ok(await identity.GetAuthenticationAsync(id, username, ct));
        }).RequireRateLimiting("login").WithSummary("Authenticate an existing ERP user")
            .WithDescription("Uses server-only legacy credential compatibility; never returns credential material.");
        app.MapGet("/api/session", async (HttpContext context, IIdentityService identity, CancellationToken ct) =>
            TypedResults.Ok(await identity.GetAuthenticationAsync(context.User.FindFirstValue("sid")!, context.User.Identity!.Name!, ct)))
            .RequireAuthorization().WithSummary("Get current identity and effective grants")
            .WithDescription("Returns a verified identity and optional selected context; selection is required before ERP modules.");
        app.MapGet("/api/companies", async (HttpContext context, IIdentityService identity, CancellationToken ct) =>
            TypedResults.Ok(await identity.CompanySelectionAsync(context.User.FindFirstValue("sid")!, context.User.Identity!.Name!, ct)))
            .RequireAuthorization().WithSummary("List authorized company/year/site choices")
            .WithDescription("Matches Windows Company/UserSite/SiteMast permissions. Unsupported database routes are unavailable, never dynamically connected.");
        app.MapPut("/api/session/context", async (SelectCompanyRequest request, HttpContext context, IIdentityService identity, CancellationToken ct) =>
            TypedResults.Ok(await identity.SelectCompanyAsync(context.User.FindFirstValue("sid")!, context.User.Identity!.Name!, request, ct)))
            .RequireAuthorization().WithSummary("Select an authorized company and site")
            .WithDescription("A single permitted site is automatic; multiple sites require a choice. Rotates context revision and rejects stale selections.");
        app.MapGet("/api/navigation", async (HttpContext context, IIdentityService identity, NavigationService navigation, CancellationToken ct) =>
        {
            var session = await identity.CurrentAsync(context, ct);
            return TypedResults.Ok(await navigation.GetAsync(session, ct));
        }).RequireAuthorization().WithSummary("Get authorized desktop module/menu navigation")
            .WithDescription("Uses current company/global grants, ReportFor placement, Parent hierarchy and system restrictions. Only registered web features have executable routes.");
        app.MapDelete("/api/session", async (HttpContext context, IIdentityService identity, CancellationToken ct) =>
        {
            await identity.RevokeAsync(context.User.FindFirstValue("sid")!, ct);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return TypedResults.NoContent();
        }).RequireAuthorization().WithSummary("Revoke the current session").WithDescription("Invalidates server access, not only the browser cookie.");
    }
}
