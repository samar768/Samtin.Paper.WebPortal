using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Tests;

public sealed class ValidationTests
{
    [Fact]
    public async Task Problem_details_include_machine_readable_fields_and_trace()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "validation-test" };
        context.Response.Body = new MemoryStream();
        var fault = ApiFault.At(422, "invalid_conversion", "Conversion requires review.",
            "lines[1].ItemCode", "lines[1].QtySDU");
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);
        Assert.True(await handler.TryHandleAsync(context, fault, TestContext.Current.CancellationToken));
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(422, context.Response.StatusCode);
        Assert.Equal("validation-test", json.RootElement.GetProperty("traceId").GetString());
        Assert.Equal("Conversion requires review.", json.RootElement.GetProperty("errors").GetProperty("lines[1].QtySDU")[0].GetString());
    }

    [Fact]
    public void Expense_numeric_errors_identify_the_exact_editable_cell()
    {
        var definition = new Dictionary<string, string?> { ["SNo"] = "1", ["PerEditable"] = "Y", ["ExpShortCode"] = "DISC" };
        var fault = Assert.Throws<ApiFault>(() => ExpenseCalculator.Calculate([definition],
            [new() { ["SNo"] = "1", ["Per"] = "invalid" }], 100, "lineExpenses[2]"));
        Assert.Contains("lineExpenses[2][0].Per", fault.Errors!.Keys);
    }
}
