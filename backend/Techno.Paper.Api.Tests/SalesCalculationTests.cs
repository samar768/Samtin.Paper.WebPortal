using Microsoft.VisualBasic;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Tests;

public sealed class SalesCalculationTests
{
    private static Dictionary<string, string?> Line(string qty = "0", string reels = "0", string size = "20", string unit = "CM",
        string rate = "2", string secondary = "0") => new()
    {
        ["Qty"] = qty, ["Reel"] = reels, ["SizeReel"] = size, ["SizeUnit"] = unit, ["Rate"] = rate, ["QtySDU"] = secondary
    };
    private static readonly SalesCalculationSettings Settings = new(14.75m, 5.8m, 0);

    [Fact]
    public void Secondary_weight_multiplies_conversion_and_controls_amount()
    {
        var result = SalesLineCalculator.Calculate(Line(qty: "10", secondary: "3.1256"), Settings, 2.5m, "lines[0]", true);
        Assert.Equal(7.814m, result.Quantity);
        Assert.Equal(15.63m, result.Amount);
    }

    [Theory]
    [InlineData("1.225", "1.23")]
    [InlineData("1.235", "1.24")]
    public void Amount_rounding_matches_VB_Format(string rate, string expected)
    {
        var result = SalesLineCalculator.Calculate(Line(qty: "1", rate: rate), Settings, 0, "lines[0]", true);
        var formatted = Strings.Format(double.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), "0.00");
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(formatted, System.Globalization.CultureInfo.CurrentCulture));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result.Amount);
    }

    [Theory]
    [InlineData("MM", "205", "20.5", "8.071")]
    [InlineData("CM", "20.5", "20.5", "8.071")]
    [InlineData("INCH", "8", "20.32", "8")]
    public void Size_and_reel_weight_follow_configured_rounding(string unit, string size, string cm, string inch)
    {
        var settings = Settings with { WeightPlaces = 2 };
        var result = SalesLineCalculator.Calculate(Line(reels: "3", size: size, unit: unit), settings, 0, "lines[0]", true);
        Assert.Equal(decimal.Parse(cm, System.Globalization.CultureInfo.InvariantCulture), result.SizeCm);
        Assert.Equal(decimal.Parse(inch, System.Globalization.CultureInfo.InvariantCulture), result.SizeInch);
        Assert.Equal(unit == "INCH" ? 354m : 356.7m, result.Quantity);
    }

    [Fact]
    public void Derives_only_a_zero_counterpart_and_allows_partial_drafts()
    {
        Assert.Equal(2m, SalesLineCalculator.Calculate(Line(qty: "232"), Settings, 0, "lines[0]", true).Reels);
        Assert.Equal(9m, SalesLineCalculator.Calculate(Line(qty: "232", reels: "9"), Settings, 0, "lines[0]", true).Reels);
        Assert.Equal(0m, SalesLineCalculator.Calculate(Line(size: "0"), Settings, 0, "lines[0]", false).Amount);
        Assert.Throws<ApiFault>(() => SalesLineCalculator.Calculate(Line(size: "0"), Settings, 0, "lines[0]", true));
        Assert.Throws<ApiFault>(() => SalesLineCalculator.Calculate(Line(secondary: "1"), Settings, 0, "lines[0]", false));
    }

    [Fact]
    public void Tolerance_percentages_round_quantities_and_compute_limits()
    {
        var result = QuantityToleranceCalculator.Calculate(new("123.456", "99", "99", "2.125", "1.25"));
        Assert.Equal("2.623", result.Excess);
        Assert.Equal("1.543", result.Shortage);
        Assert.Equal("126.079", result.UpperQuantity);
        Assert.Equal("121.913", result.LowerQuantity);
        var zeroPercent = QuantityToleranceCalculator.Calculate(new("100", "99", "99", "0", "0"));
        Assert.Equal("0", zeroPercent.Excess); Assert.Equal("0", zeroPercent.Shortage);
    }

    [Fact]
    public void Tolerance_quantity_mode_rejects_negative_or_excessive_shortage()
    {
        Assert.Equal("95.875", QuantityToleranceCalculator.Calculate(new("100", "2.5", "4.125", null, null)).LowerQuantity);
        Assert.Throws<ApiFault>(() => QuantityToleranceCalculator.Calculate(new("100", "0", "100.001", null, null)));
        Assert.Throws<ApiFault>(() => QuantityToleranceCalculator.Calculate(new("100", "-1", "0", null, null)));
        var failure = Assert.Throws<ApiFault>(() => QuantityToleranceCalculator.Validate(100,
            new() { ["Tl_ExcessQty"] = "0", ["TL_ShortageQty"] = "101" }, "lines[0]"));
        Assert.NotNull(failure.Errors);
        Assert.Single(failure.Errors);
        Assert.Contains("lines[0].TL_ShortageQty", failure.Errors.Keys);
    }
}
