using Microsoft.AspNetCore.Mvc;
using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Endpoints;

public static class OrderEndpoints
{
    private static Task<SessionResponse> Current(HttpContext context, IIdentityService identity, CancellationToken ct) =>
        identity.CurrentAsync(context, ct);

    public static void MapOrders(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/orders", async (string? search, int? page, int? pageSize, string? sort, string? direction,
            HttpContext context, IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.ListAsync(await Current(context, identity, ct), search, page ?? 0, ct,
                pageSize ?? 25, sort ?? "date", direction ?? "desc")))
            .WithSummary("Search authorized Sales orders")
            .WithDescription("Scoped to the current context/view window. Page sizes: 10/25/50/100; allowlisted sort/direction, stable DocId tiebreaker. Defaults remain 25, date descending.");
        api.MapGet("/orders/voucher-types", async (HttpContext context, IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.VoucherTypesAsync(await Current(context, identity, ct), ct)))
            .WithSummary("List active Sales Order vouchers and portal save eligibility");
        api.MapGet("/orders/defaults", async (string? voucherType, HttpContext context, IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.DefaultsAsync(await Current(context, identity, ct), ct, voucherType)))
            .WithSummary("Get a new order template").WithDescription("Includes active fields, expense definitions and configured terms.");
        api.MapGet("/orders/line-defaults", async (string? voucherType, HttpContext context, IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.LineDefaultsAsync(await Current(context, identity, ct), ct, voucherType)))
            .WithSummary("Get defaults for a new order line").WithDescription("Uses current authorized field metadata and expense configuration.");
        api.MapGet("/orders/item-expense-defaults", async (string itemCode, HttpContext context,
            IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.ItemExpenseDefaultsAsync(await Current(context, identity, ct), itemCode, ct)))
            .WithSummary("Get tax-band defaults for a selected Sales Order item")
            .WithDescription("Returns only rate and amount defaults for a finished item available in the active site.");
        api.MapGet("/orders/{id}", async (string id, HttpContext context, IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.GetAsync(await Current(context, identity, ct), id, ct)))
            .WithSummary("Read a persisted order aggregate").WithDescription("Returns a concurrency token and explicit record-state locks.");
        api.MapGet("/orders/{id}/print.pdf", async (string id, HttpContext context, IIdentityService identity,
            IOrderService orders, ICrystalSalesOrderRenderer renderer, CancellationToken ct) =>
        {
            using var data = await orders.LoadPrintDataAsync(await Current(context, identity, ct), id, ct);
            var pdf = await renderer.ExportAsync(data, ct);
            context.Response.Headers.CacheControl = "private, no-store";
            return TypedResults.File(pdf, "application/pdf", "Sales-Order-Sample.pdf");
        }).WithSummary("Download the Crystal Sales Order sample PDF")
          .WithDescription("Development-only. Uses the saved order and requires View/Print rights in the selected context.")
          .Produces<byte[]>(StatusCodes.Status200OK, contentType: "application/pdf")
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
          .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        api.MapGet("/lookups/{key}", async (string key, string? search, HttpContext context, IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.LookupAsync(await Current(context, identity, ct), key, search, ct)))
            .WithSummary("Search an approved Sales lookup").WithDescription("Lookup identifiers are server allowlisted and results are bounded.");
        api.MapGet("/orders/{id}/approval", async (string id, HttpContext context, IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.ApprovalAsync(await Current(context, identity, ct), id, ct)))
            .WithSummary("Read Sales Order approval progress").WithDescription("Requires order View/date access; returns snapshotted sequential stages and actor eligibility.");
        api.MapGet("/orders/{id}/copy", async (string id, HttpContext context, IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.CopyAsync(await Current(context, identity, ct), id, ct)))
            .WithSummary("Copy an authorized order into an unsaved draft")
            .WithDescription("Requires View/Add and a writable workspace. Fresh numbering/dates, current metadata/definitions, no source identities, approval, allocations or customer PO reference. Does not persist anything.");
        api.MapPost("/orders/{id}/unapprove", async (string id, UnapproveOrderRequest request, HttpContext context,
            IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.UnapproveAsync(await Current(context, identity, ct), id, request, ct)))
            .WithSummary("Reverse an eligible order approval with a reason")
            .WithDescription("Requires dedicated group CanReverse plus ERP approval rights, source/workflow versions and date access. Closed, downstream-used or allocated orders are blocked. The next save starts a new request from stage one.");
        api.MapPost("/orders/{id}/approval", async (string id, OrderDecisionRequest request, HttpContext context,
            IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.DecideAsync(await Current(context, identity, ct), id, request, ct)))
            .WithSummary("Approve or reject the active Sales Order stage")
            .WithDescription("Checks ERP Approved By permission, configured group, workspace, document/request versions and self/repeat policy. Only final approval sets SOrd.ApprovedBy.");
        api.MapPost("/orders/calculate", async (string? id, SaveOrderRequest request, HttpContext context,
            IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.SaveAsync(await Current(context, identity, ct), id, request, true, ct)))
            .WithSummary("Validate and calculate an order").WithDescription("Uses the same server rules as save; rolls back without committing business changes.");
        api.MapPost("/orders/draft-calculation", async (DraftCalculationRequest request, HttpContext context,
            IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.CalculateDraftAsync(await Current(context, identity, ct), request, ct)))
            .WithSummary("Calculate an incomplete Sales draft")
            .WithDescription("Uses shared decimal line/expense rules with current site configuration. Does not require complete header/lookup entry, reserve numbers, change tasks or persist any values. Save retains full validation.");
        api.MapPost("/orders/tolerance", async (ToleranceRequest request, HttpContext context,
            IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.CalculateToleranceAsync(await Current(context, identity, ct), request, ct)))
            .WithSummary("Calculate per-row quantity tolerances")
            .WithDescription("Exact-decimal percent-to-quantity conversion and upper/lower limits. Null percentages use quantities directly. Rejects negative tolerances and shortages above order quantity; does not persist values.");
        api.MapPost("/orders", async (SaveOrderRequest request, HttpContext context,
            IIdentityService identity, IOrderService orders, CancellationToken ct) =>
        {
            var saved = await orders.SaveAsync(await Current(context, identity, ct), null, request, false, ct);
            return TypedResults.Created($"/api/orders/{Uri.EscapeDataString(saved.Id!)}", saved);
        }).WithSummary("Create an order atomically").WithDescription("Requires Add, current metadata and a UUID idempotency key.");
        api.MapPut("/orders/{id}", async (string id, SaveOrderRequest request, HttpContext context,
            IIdentityService identity, IOrderService orders, CancellationToken ct) =>
            TypedResults.Ok(await orders.SaveAsync(await Current(context, identity, ct), id, request, false, ct)))
            .WithSummary("Update an eligible order atomically").WithDescription("Requires Edit and an unchanged aggregate version; protected values are preserved.");
        api.MapDelete("/orders/{id}", async (string id, [FromBody] DeleteOrderRequest request, HttpContext context,
            IIdentityService identity, IOrderService orders, CancellationToken ct) =>
        {
            await orders.DeleteAsync(await Current(context, identity, ct), id, request, ct);
            return TypedResults.NoContent();
        }).WithSummary("Delete one eligible order atomically").WithDescription("Requires Delete, concurrency match and no approval/downstream/allocation lock.");
        api.MapGet("/metadata/orders", async (HttpContext context, IIdentityService identity, IMetadataService metadata, CancellationToken ct) =>
            TypedResults.Ok(await metadata.GetAsync(await Current(context, identity, ct), ct)))
            .WithSummary("Get authorized order fields").WithDescription("Returns an active schema version shared by forms/defaults/get/create/update.");
        api.MapGet("/metadata/discover", async (HttpContext context, IIdentityService identity, IMetadataService metadata, CancellationToken ct) =>
        {
            if (!(await Current(context, identity, ct)).Capabilities.MetadataAdministration)
                throw new ApiFault(403, "metadata_denied", "Metadata administration is not permitted.");
            return TypedResults.Ok(await metadata.DiscoverAsync(ct));
        }).WithSummary("Discover current order column shapes").WithDescription("Administrator-only; discovery does not activate fields or alter business tables.");
        api.MapPost("/metadata/activate", async (ActivateFieldRequest request, HttpContext context,
            IIdentityService identity, IMetadataService metadata, CancellationToken ct) =>
        {
            await metadata.ActivateAsync(await Current(context, identity, ct), request, ct);
            return TypedResults.NoContent();
        }).WithSummary("Activate an eligible extension field").WithDescription("Administrator-only metadata approval; no application deployment or business DDL.");
    }
}
