using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Endpoints;

public static class LookupEndpoints
{
    public static void MapValueHelp(this WebApplication app)
    {
        var api = app.MapGroup("/api/value-help").RequireAuthorization();
        api.MapGet("/{id}", async (string id, HttpContext context, IIdentityService identity, ILookupService lookups, CancellationToken ct) =>
            TypedResults.Ok(await lookups.DefinitionAsync(await identity.CurrentAsync(context, ct), id, ct)))
            .WithSummary("Read authorized SQL-driven lookup metadata")
            .WithDescription("Only enabled registered lookups with a known server resource policy are available. No SQL/source definitions are returned.");
        api.MapGet("/{id}/options", async (string id, string? search, int? page, HttpContext context,
            IIdentityService identity, ILookupService lookups, CancellationToken ct) =>
            TypedResults.Ok(await lookups.SearchAsync(await identity.CurrentAsync(context, ct), id, search, page ?? 0, ct)))
            .WithSummary("Search one bounded value-help table page")
            .WithDescription("Uses configured search/display columns, stable key tiebreaking and mandatory source scope. All user inputs are SQL parameters.");
        api.MapPost("/{id}/resolve", async (string id, ResolveLookupRequest request, HttpContext context,
            IIdentityService identity, ILookupService lookups, CancellationToken ct) =>
            TypedResults.Ok(await lookups.ResolveAsync(await identity.CurrentAsync(context, ct), id, request, ct)))
            .WithSummary("Resolve saved reference descriptions by exact keys")
            .WithDescription("Resolves up to 100 keys in one scoped query, including keys outside the first search page. Missing keys are explicit; no values are persisted.");
    }
}
