using System.Net;

namespace Techno.Paper.Api.Endpoints;

internal static class DevelopmentSwaggerEndpoints
{
    public static void MapDevelopmentSwagger(this WebApplication app)
    {
        var root = app.Environment.ContentRootPath;
        var assets = Path.GetFullPath(Path.Combine(root, "..", "..", "node_modules", "swagger-ui-dist"));
        var swagger = app.MapGroup("/api/swagger").ExcludeFromDescription();
        swagger.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote))
                return TypedResults.NotFound();

            return await next(invocation);
        });

        swagger.MapOpenApi("/{documentName}.json");
        swagger.MapGet("/{asset}", (string asset) => asset switch
        {
            "index.html" => Serve(Path.Combine(root, "Development", "swagger.html"), "text/html; charset=utf-8"),
            "setup.js" => Serve(Path.Combine(root, "Development", "swagger.js"), "text/javascript; charset=utf-8"),
            "swagger-ui-bundle.js" => Serve(Path.Combine(assets, asset), "text/javascript; charset=utf-8"),
            "swagger-ui.css" => Serve(Path.Combine(assets, asset), "text/css; charset=utf-8"),
            _ => TypedResults.NotFound(),
        });
    }

    private static IResult Serve(string file, string contentType) => File.Exists(file)
        ? TypedResults.PhysicalFile(file, contentType)
        : TypedResults.Problem("Swagger UI files are missing. Run npm ci in the Samtin repository root and start the API from its project folder.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
