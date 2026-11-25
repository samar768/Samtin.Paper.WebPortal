using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Endpoints;

public static class TaskEndpoints
{
    public static void MapTasks(this WebApplication app)
    {
        var api = app.MapGroup("/api/tasks").RequireAuthorization();
        api.MapGet("", async (int? page, HttpContext context, IIdentityService identity, ITaskService tasks, CancellationToken ct) =>
            TypedResults.Ok(await tasks.ListAsync(await identity.CurrentAsync(context, ct), page ?? 0, ct)))
            .WithSummary("List authorized workspace tasks").WithDescription("Returns at most 25 tasks, next-page availability and unread active count. Applies groups, record visibility and workspace boundaries.");
        api.MapGet("/types", async (HttpContext context, IIdentityService identity, ITaskService tasks, CancellationToken ct) =>
            TypedResults.Ok(await tasks.TypesAsync(await identity.CurrentAsync(context, ct), ct)))
            .WithSummary("List manually publishable task types").WithDescription("Only configured memo/note publishing grants; transaction/approval events are server-owned.");
        api.MapGet("/{id:guid}", async (Guid id, HttpContext context, IIdentityService identity, ITaskService tasks, CancellationToken ct) =>
            TypedResults.Ok(await tasks.GetAsync(await identity.CurrentAsync(context, ct), id, ct)))
            .WithSummary("Read an authorized task").WithDescription("Unauthorized, unregistered or wrong-context targets return 404.");
        api.MapPost("", async (PublishTaskRequest request, HttpContext context, IIdentityService identity, ITaskService tasks, CancellationToken ct) =>
        {
            var task = await tasks.PublishAsync(await identity.CurrentAsync(context, ct), request, ct);
            return TypedResults.Created($"/api/tasks/{task.Id}", task);
        }).WithSummary("Publish a memo or note").WithDescription("Audience is resolved from database configuration. Scope, identity and routes cannot be supplied by clients. Retries use an idempotency key.");
        api.MapPost("/{id:guid}/read", async (Guid id, HttpContext context, IIdentityService identity, ITaskService tasks, CancellationToken ct) =>
        {
            await tasks.ReadAsync(await identity.CurrentAsync(context, ct), id, ct);
            return TypedResults.NoContent();
        }).WithSummary("Mark a task read for this user").WithDescription("Idempotent per-user receipt; does not complete an approval or mark other users' notifications read.");
    }
}
