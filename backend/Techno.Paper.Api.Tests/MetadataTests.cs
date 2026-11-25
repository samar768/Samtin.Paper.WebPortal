using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Tests;

public sealed class MetadataTests
{
    [Theory]
    [InlineData("((12.34))", "decimal", "12.34")]
    [InlineData("(N'Buyer''s note')", "nvarchar", "Buyer's note")]
    [InlineData("((1))", "bit", "true")]
    [InlineData("(NULL)", "varchar", null)]
    public void Literal_defaults_are_parsed_without_sql_execution(string expression, string type, string? expected) =>
        Assert.Equal(expected, MetadataService.LiteralDefault(expression, type));

    [Fact]
    public void Sql_default_expressions_are_not_executed() =>
        Assert.Throws<ApiFault>(() => MetadataService.LiteralDefault("(dbo.RunAnything())", "varchar"));
}
