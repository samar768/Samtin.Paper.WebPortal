using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Tests;

public sealed class SalesOrderPrintTests
{
    [Fact]
    public void ProcedureReceivesOneLiteralWithInternalNumberingSpacesPreserved()
    {
        Assert.Equal("'DHOSORD  2026     484'", OrderService.LegacyPrintArgument("DHOSORD  2026     484"));
        Assert.Equal(50, OrderService.LegacyPrintArgument(new string('A', 48)).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("ORD'1")]
    [InlineData("ORD1,ORD2")]
    [InlineData("ORD1\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void ProcedureArgumentRejectsUnsupportedIdentifiers(string? id)
    {
        Assert.Throws<ApiFault>(() => OrderService.LegacyPrintArgument(id));
    }
}
