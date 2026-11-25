using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Tests;

public sealed class CompanyContextTests
{
    [Theory]
    [InlineData("|HO|,|B1|", "HO,B1")]
    [InlineData("|HO|;|B1|", "HO,B1")]
    [InlineData("|HO|", "HO")]
    [InlineData("", "")]
    public void Legacy_site_grants_are_parsed_as_exact_codes(string grant, string expected) =>
        Assert.Equal(expected, string.Join(",", CompanyContextRules.ParseSites(grant)));

    [Fact]
    public void Only_one_authorized_site_is_automatic_and_multiple_sites_require_a_choice()
    {
        SiteOptionResponse[] sites = [new("HO", "Head Office", true), new("B1", "Branch", false)];
        Assert.Equal("HO", CompanyContextRules.SelectSite([sites[0]], null).Code);
        Assert.Equal("B1", CompanyContextRules.SelectSite(sites, "B1").Code);
        Assert.Equal(422, Assert.Throws<ApiFault>(() => CompanyContextRules.SelectSite(sites, null)).Status);
        Assert.Equal(403, Assert.Throws<ApiFault>(() => CompanyContextRules.SelectSite(sites, "XX")).Status);
        Assert.Throws<ApiFault>(() => CompanyContextRules.SelectSite([], null));
    }

    [Theory]
    [InlineData("SA", true)]
    [InlineData("super", true)]
    [InlineData("SALES", false)]
    public void Windows_superusers_are_exact_names(string username, bool expected) =>
        Assert.Equal(expected, CompanyContextRules.IsSuperuser(username));
}
