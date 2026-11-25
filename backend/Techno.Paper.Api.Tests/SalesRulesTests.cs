using Techno.Paper.Api.Domain;

namespace Techno.Paper.Api.Tests;

public sealed class SalesRulesTests
{
    [Fact]
    public void Document_number_retains_fixed_width_legacy_format()
    {
        var id = SalesRules.DocumentId("HO", "SORD", "2026", 12);
        Assert.Equal(21, id.Length);
        Assert.Equal("DHO" + "SORD " + " 2026" + "      12", id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000000)]
    public void Document_number_rejects_overflow_and_zero(long number) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesRules.DocumentId("HO", "SORD", "2026", number));

    [Fact]
    public void Identical_grants_are_not_an_ambiguity()
    {
        Assert.Equal("AEDP", SalesRules.ResolveGrant(["AEDP", "AEDP"]));
        Assert.Throws<PolicyConflictException>(() => SalesRules.ResolveGrant(["A", "AE"]));
        Assert.Null(SalesRules.ResolveGrant([]));
    }

    [Fact]
    public void Legacy_verifier_checks_username_and_case_insensitive_password()
    {
        // Synthetic fixture, not an ERP credential.
        var stored = ((char)37) + new string("DEMOEXAMPLE".Select(c => (char)(c + 37)).ToArray());
        Assert.True(LegacyCredentials.Verify(" demo ", " example ", stored));
        Assert.False(LegacyCredentials.Verify("other", "example", stored));
        Assert.False(LegacyCredentials.Verify("demo", "wrong", stored));
        Assert.False(LegacyCredentials.Verify("demo", "example", ""));
    }

    [Theory]
    [InlineData("12.34", 5, 2, true)]
    [InlineData("1234.56", 5, 2, false)]
    [InlineData("1.234", 5, 2, false)]
    [InlineData("not-a-number", 5, 2, false)]
    public void Decimal_validation_does_not_silently_truncate(string value, int precision, int scale, bool valid) =>
        Assert.Equal(valid, SalesRules.ValidDecimal(value, precision, scale));
}
