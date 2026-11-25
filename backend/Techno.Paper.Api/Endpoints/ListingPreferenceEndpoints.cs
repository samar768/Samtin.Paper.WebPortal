using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Endpoints;

public static class ListingPreferenceEndpoints
{
    private static Task<SessionResponse> Current(HttpContext context, IIdentityService identity, CancellationToken ct) =>
        identity.CurrentAsync(context, ct);
    public static void MapListingPreferences(this WebApplication app)
    {
        var api = app.MapGroup("/api/preferences/listings").RequireAuthorization();
        api.MapGet("/{listing}", async (string listing, HttpContext context, IIdentityService identity,
            IListingPreferenceService preferences, CancellationToken ct) =>
            TypedResults.Ok(await preferences.GetAsync(
                await Current(context, identity, ct), listing, ct)))
            .WithSummary("Get personal listing settings")
            .WithDescription("Scoped to the authenticated user and company. Returns defaults when no preference exists.");
        api.MapPut("/{listing}", async (string listing, ListingPreferences request, HttpContext context, IIdentityService identity,
            IListingPreferenceService preferences, CancellationToken ct) =>
            TypedResults.Ok(await preferences.SaveAsync(
                await Current(context, identity, ct), listing, request, ct)))
            .WithSummary("Save personal listing settings")
            .WithDescription("Validates allowlisted columns, widths, density, page size and sorting. Last explicit save wins; never accepts a user/company selector.");
        api.MapDelete("/{listing}", async (string listing, HttpContext context, IIdentityService identity,
            IListingPreferenceService preferences, CancellationToken ct) =>
        {
            await preferences.ResetAsync(await Current(context, identity, ct), listing, ct);
            return TypedResults.NoContent();
        }).WithSummary("Reset personal listing settings")
            .WithDescription("Deletes only the current user's preference for this company/listing; orders are untouched.");
    }
}
