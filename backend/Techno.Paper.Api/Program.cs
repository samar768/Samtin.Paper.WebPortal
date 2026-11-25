using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Techno.Paper.Api.Endpoints;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
if (!builder.Environment.IsDevelopment() && !builder.Configuration.GetValue<bool>("Poc:AllowHostedDevelopmentDatabase"))
    throw new InvalidOperationException("This development-database POC cannot run in production.");
var options = builder.Configuration.GetSection("Poc").Get<PocOptions>() ?? new PocOptions();
if (string.IsNullOrWhiteSpace(options.ControlConnection) || string.IsNullOrWhiteSpace(options.TransactionConnection))
    throw new InvalidOperationException("Configure server-only Poc connection settings before starting the API.");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<Database>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<ICompanyContextService, CompanyContextService>();
builder.Services.AddScoped<NavigationService>();
builder.Services.AddScoped<IMetadataService, MetadataService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<ICrystalSalesOrderRenderer, CrystalSalesOrderRenderer>();
builder.Services.AddScoped<ILookupService, LookupService>();
builder.Services.AddScoped<IMasterService, MasterService>();
builder.Services.AddScoped<IEnvironmentService, EnvironmentService>();
builder.Services.AddSingleton<ILookupAccessPolicy, SalesLookupAccessPolicy>();
builder.Services.AddScoped<ILookupAccessPolicy, EnvironmentLookupAccessPolicy>();
builder.Services.AddScoped<IListingPreferenceService, ListingPreferenceService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ITaskTarget, SalesOrderTaskTarget>();
builder.Services.AddSingleton<IWriteCheckpoint, WriteCheckpoint>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    json.SerializerOptions.RespectRequiredConstructorParameters = true;
    json.SerializerOptions.RespectNullableAnnotations = true;
    json.SerializerOptions.AllowDuplicateProperties = false;
});
builder.Services.AddAntiforgery(anti =>
{
    anti.HeaderName = "X-CSRF-TOKEN";
    anti.Cookie.Name = "PaperPoc.Csrf";
    anti.Cookie.HttpOnly = true;
    anti.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(cookie =>
{
    cookie.Cookie.Name = "PaperPoc.Session";
    cookie.Cookie.HttpOnly = true;
    cookie.Cookie.SameSite = SameSiteMode.Strict;
    cookie.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    cookie.SlidingExpiration = true;
    cookie.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    cookie.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    cookie.Events.OnValidatePrincipal = async context =>
    {
        var id = context.Principal?.FindFirstValue("sid");
        var username = context.Principal?.Identity?.Name;
        if (id is null || username is null || !await context.HttpContext.RequestServices
            .GetRequiredService<IIdentityService>().IsSessionValidAsync(id, username, context.HttpContext.RequestAborted))
            context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(rate =>
{
    rate.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rate.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = 262144);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 262144);
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        if (!string.Equals(context.Request.Headers.Origin, options.PublicOrigin, StringComparison.Ordinal))
            throw new ApiFault(403, "invalid_origin", "The request origin is not permitted.");
        await context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
            .ValidateRequestAsync(context);
    }
    await next(context);
});
app.MapOpenApi();
if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Poc:EnableSwaggerUi"))
    app.MapDevelopmentSwagger();
app.MapSession();
app.MapOrders();
app.MapValueHelp();
app.MapMasters();
app.MapEnvironment();
app.MapListingPreferences();
app.MapTasks();
app.Run();

public partial class Program;
