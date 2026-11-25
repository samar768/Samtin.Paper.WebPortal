using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Techno.Paper.Api.Domain;

namespace Techno.Paper.Api.Middleware;

public sealed class ApiFault(int status, string code, string message,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    public static ApiFault At(int status, string code, string message, params string[] paths) =>
        new(status, code, message, paths.ToDictionary(path => path, _ => new[] { message }));
}

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, message) = exception switch
        {
            ApiFault fault => (fault.Status, fault.Code, fault.Message),
            PolicyConflictException => (409, "permission_configuration", "Conflicting ERP grants require administrator review."),
            Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException => (403, "csrf_failed", "The request token is invalid. Refresh and retry."),
            _ => (503, "service_unavailable", "The operation could not complete. Please retry or contact the administrator.")
        };
        if (exception is ApiFault or PolicyConflictException)
            logger.LogWarning("Request rejected: {Code}; trace {TraceId}", code, context.TraceIdentifier);
        else
            logger.LogError(exception, "Request failed; trace {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status, Title = code, Detail = message,
            Extensions = { ["traceId"] = context.TraceIdentifier }
        };
        if (exception is ApiFault { Errors: not null } validation) problem.Extensions["errors"] = validation.Errors;
        await context.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
