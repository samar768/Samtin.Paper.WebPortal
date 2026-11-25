using Microsoft.AspNetCore.Mvc;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Endpoints;

public static class MasterEndpoints
{
    public static void MapMasters(this WebApplication app)
    {
        var api = app.MapGroup("/api/masters").RequireAuthorization();
        api.MapGet("/{id}/form", async (string id, string? recordId, HttpContext context,
            IIdentityService identity, IMasterService masters, CancellationToken ct) =>
            TypedResults.Ok(await masters.FormAsync(await identity.CurrentAsync(context, ct), id, recordId, ct)));
        api.MapGet("/{id}/records", async (string id, string? search, int? page, HttpContext context,
            IIdentityService identity, IMasterService masters, CancellationToken ct) =>
            TypedResults.Ok(await masters.ListAsync(await identity.CurrentAsync(context, ct), id, search, page ?? 0, ct)));
        api.MapPost("/{id}/records", async (string id, SaveMasterRequest request, HttpContext context,
            IIdentityService identity, IMasterService masters, CancellationToken ct) =>
            TypedResults.Ok(await masters.SaveAsync(await identity.CurrentAsync(context, ct), id, null, request, ct)));
        api.MapPut("/{id}/records/{recordId}", async (string id, string recordId, SaveMasterRequest request, HttpContext context,
            IIdentityService identity, IMasterService masters, CancellationToken ct) =>
            TypedResults.Ok(await masters.SaveAsync(await identity.CurrentAsync(context, ct), id, recordId, request, ct)));
        api.MapDelete("/{id}/records/{recordId}", async (string id, string recordId, [FromBody] DeleteOrderRequest request, HttpContext context,
            IIdentityService identity, IMasterService masters, CancellationToken ct) =>
        {
            await masters.DeleteAsync(await identity.CurrentAsync(context, ct), id, recordId, request, ct);
            return TypedResults.NoContent();
        });
        api.MapGet("/{id}/print", async (string id, string? search, HttpContext context,
            IIdentityService identity, IMasterService masters, CancellationToken ct) =>
            TypedResults.Ok(await masters.PrintAsync(await identity.CurrentAsync(context, ct), id, search, ct)));
        api.MapGet("/{id}/audit", async (string id, string? recordId, int? page, HttpContext context,
            IIdentityService identity, IMasterService masters, CancellationToken ct) =>
            TypedResults.Ok(await masters.AuditAsync(await identity.CurrentAsync(context, ct), id, recordId, page ?? 0, ct)));
    }
}
