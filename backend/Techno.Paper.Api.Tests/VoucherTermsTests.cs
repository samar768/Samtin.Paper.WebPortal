using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Tests;

public sealed class VoucherTermsTests
{
    private static Dictionary<string, string?> Distance() => new()
    {
        ["Code"] = "72", ["VType"] = "OTHER", ["VoucherCategory"] = "SALES", ["TCManualCode"] = "DELIVERYZONE", ["TCFieldDataType"] = "5",
        ["SaveIn"] = null, ["TableName"] = "SOrd", ["Mandatory"] = "Y",
        ["DMHGQuery"] = "Select  ZoneCode, ZoneName from ZoneMaster", ["LookupId"] = "terms-OTHER-72", ["Editor"] = "lookup"
    };

    [Fact]
    public async Task Selected_key_resolves_canonical_code_and_description_alongside_text()
    {
        var distance = Distance();
        var text = new Dictionary<string, string?> { ["Code"] = "1", ["Editor"] = "text", ["SaveIn"] = null, ["TableName"] = "SOrd", ["Mandatory"] = "N" };
        await VoucherTerms.ApplyAsync([text, distance],
            [new() { ["Code"] = "1", ["Value"] = "Freight prepaid" }, new() { ["Code"] = "72", ["ValueCode"] = "d10 " }], "SOrd",
            (id, key) =>
            {
                Assert.Equal("terms-OTHER-72", id);
                Assert.Equal("d10", key);
                return Task.FromResult<(string?, string?)>(("D10", "Within 10 km"));
            });
        Assert.Equal("D10", distance["ValueCode"]);
        Assert.Equal("Within 10 km", distance["Value"]);
        Assert.Equal("Freight prepaid", text["Value"]);
    }

    [Theory]
    [InlineData(null, "term_required")]
    [InlineData("UNKNOWN", "invalid_term_selection")]
    public async Task Mandatory_distance_rejects_missing_or_unknown_keys(string? key, string expected)
    {
        var error = await Assert.ThrowsAsync<ApiFault>(() => VoucherTerms.ApplyAsync([Distance()],
            [new() { ["Code"] = "72", ["ValueCode"] = key }], "SOrd", (_, _) => Task.FromResult<(string?, string?)>((null, null))));
        Assert.Equal(expected, error.Code);
    }

    [Fact]
    public async Task Only_the_reviewed_lookup_is_supported_and_the_browser_cannot_set_the_label()
    {
        var distance = Distance();
        distance.Remove("LookupId");
        var unreviewed = await Assert.ThrowsAsync<ApiFault>(() => VoucherTerms.ApplyAsync([distance], [], "SOrd",
            (_, _) => throw new InvalidOperationException()));
        Assert.Equal("unsupported_terms", unreviewed.Code);

        var invalidInput = await Assert.ThrowsAsync<ApiFault>(() => VoucherTerms.ApplyAsync([Distance()],
            [new() { ["Code"] = "72", ["ValueCode"] = "D10", ["Value"] = "Arbitrary label" }], "SOrd",
            (_, _) => throw new InvalidOperationException()));
        Assert.Equal("invalid_terms", invalidInput.Code);
    }

    [Theory]
    [InlineData("Select top 30 Code,Name from itemmast", "dbo", "itemmast", "Code", "Name", 30, false, false)]
    [InlineData("SELECT TOP (42) [Id], [Title] FROM [ref].[Zone] ORDER BY Title DESC;", "ref", "Zone", "Id", "Title", 42, true, true)]
    [InlineData("SELECT KeyCode, Description FROM Carrier", "dbo", "Carrier", "KeyCode", "Description", null, false, false)]
    public void Parses_source_columns_and_limit_without_a_term_specific_rule(
        string query, string schema, string table, string key, string label, int? limit, bool sortByLabel, bool descending)
    {
        Assert.Equal(new VoucherTermLookup.Definition(schema, table, key, label, limit, sortByLabel, descending),
            VoucherTermLookup.Parse(query));
    }

    [Theory]
    [InlineData("SELECT Code, Name FROM ItemMast WHERE ItemType='FM'")]
    [InlineData("SELECT Code, Name FROM ItemMast; SELECT Code, Name FROM ItemMast")]
    [InlineData("SELECT Code, Name FROM ItemMast ORDER BY OtherColumn")]
    [InlineData("SELECT TOP 10001 Code, Name FROM ItemMast")]
    public void Unsupported_shapes_are_not_rewritten_into_a_different_lookup(string query) =>
        Assert.Null(VoucherTermLookup.Parse(query));

    [Theory]
    [InlineData("date", "2026-11-24", "2026-02-30")]
    [InlineData("integer", "10", "10.5")]
    [InlineData("decimal", "10.5", "10x")]
    public async Task Scalar_terms_follow_the_configured_editor(string editor, string valid, string invalid)
    {
        var term = Distance();
        term.Remove("LookupId");
        term["Editor"] = editor;
        var input = new Dictionary<string, string?> { ["Code"] = term["Code"], ["Value"] = valid };
        await VoucherTerms.ApplyAsync([term], [input], "SOrd", (_, _) => throw new InvalidOperationException());
        Assert.Equal(valid, term["Value"]);
        input["Value"] = invalid;
        var error = await Assert.ThrowsAsync<ApiFault>(() => VoucherTerms.ApplyAsync([term], [input], "SOrd", (_, _) => throw new InvalidOperationException()));
        Assert.Equal("invalid_term_value", error.Code);
    }
}
