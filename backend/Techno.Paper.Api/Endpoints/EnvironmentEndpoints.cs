using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Endpoints;

public static class EnvironmentEndpoints
{
    public static void MapEnvironment(this WebApplication app)
    {
        var api = app.MapGroup("/api/environment-settings").RequireAuthorization();
        api.MapGet("/form", async (HttpContext context, IIdentityService identity, IEnvironmentService settings, CancellationToken ct) =>
            TypedResults.Ok(await settings.FormAsync(await identity.CurrentAsync(context, ct), ct)));
        api.MapPut("/", async (SaveMasterRequest request, HttpContext context, IIdentityService identity,
            IEnvironmentService settings, CancellationToken ct) =>
            TypedResults.Ok(await settings.SaveAsync(await identity.CurrentAsync(context, ct), request, ct)));
        api.MapGet("/audit", async (int? page, HttpContext context, IIdentityService identity, IEnvironmentService settings, CancellationToken ct) =>
            TypedResults.Ok(await settings.AuditAsync(await identity.CurrentAsync(context, ct), page ?? 0, ct)));
    }
}
