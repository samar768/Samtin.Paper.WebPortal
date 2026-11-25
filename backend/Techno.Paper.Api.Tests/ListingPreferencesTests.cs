using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Tests;

public sealed class ListingPreferencesTests
{
    [Fact]
    public void Defaults_preserve_existing_listing_and_return_independent_columns()
    {
        var defaults = ListingPolicy.Defaults();
        ListingPolicy.Validate(defaults);
        Assert.Equal(25, defaults.PageSize);
        Assert.Equal("date", defaults.SortColumn);
        Assert.Equal("desc", defaults.SortDirection);
        Assert.Equal(5, defaults.Columns.Count);
        Assert.All(defaults.Columns, column => Assert.True(column.Visible));
        Assert.NotSame(defaults.Columns, ListingPolicy.Defaults().Columns);
    }

    [Fact]
    public void Customization_accepts_reordering_hidden_columns_and_bounded_page_sizes()
    {
        var settings = ListingPolicy.Defaults();
        ListingPolicy.Validate(settings with
        {
            Columns = settings.Columns.Reverse().Select(c => c with { Visible = c.Id == "buyer", Width = 240 }).ToArray(),
            Density = "comfortable", PageSize = 100, SortColumn = "gross", SortDirection = "asc"
        });
    }

    [Fact]
    public void Invalid_settings_never_silently_replace_saved_preferences()
    {
        var settings = ListingPolicy.Defaults();
        ListingPreferences[] invalid =
        [
            settings with { SchemaVersion = 2 },
            settings with { Columns = [] },
            settings with { Columns = settings.Columns.Select(_ => settings.Columns[0]).ToArray() },
            settings with { Columns = settings.Columns.Select(c => c with { Visible = false }).ToArray() },
            settings with { Columns = settings.Columns.Select(c => c with { Width = 10000 }).ToArray() },
            settings with { Columns = settings.Columns.Select(c => c with { Id = "DocId" }).ToArray() },
            settings with { PageSize = 10000 },
            settings with { SortColumn = "date; DELETE FROM dbo.SOrd" },
            settings with { SortDirection = "descending" },
            settings with { Density = "unknown" }
        ];
        foreach (var value in invalid)
            Assert.Equal(400, Assert.Throws<ApiFault>(() => ListingPolicy.Validate(value)).Status);
    }

    [Theory]
    [InlineData("reference", "asc", "s.RecId ASC,s.DocId DESC")]
    [InlineData("date", "desc", "s.V_Date DESC,s.DocId DESC")]
    [InlineData("gross", "asc", "s.GAmount ASC,s.DocId DESC")]
    public void Sorting_uses_allowlisted_SQL_and_a_stable_tiebreaker(string key, string direction, string expected) =>
        Assert.Equal(expected, ListingPolicy.OrderBy(key, direction));

    [Fact]
    public void Invalid_sort_and_paging_are_rejected_before_SQL()
    {
        Assert.Throws<ApiFault>(() => ListingPolicy.OrderBy("s.DocId", "asc"));
        Assert.Throws<ApiFault>(() => ListingPolicy.OrderBy("date", "desc; DROP TABLE x"));
        Assert.Throws<ApiFault>(() => ListingPolicy.ValidatePageSize(26));
    }
}
