using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Domain;

public sealed record SalesCalculationSettings(decimal InchKg, decimal CmKg, int WeightPlaces);
public sealed record SalesLineResult(decimal Quantity, decimal Reels, decimal SizeCm, decimal SizeInch, decimal Amount);

public static class SalesLineCalculator
{
    public static SalesLineResult Calculate(Dictionary<string, string?> row, SalesCalculationSettings settings,
        decimal conversion, string path, bool complete, int rowNumber = 1)
    {
        decimal Number(string key) => ExpenseCalculator.Number(row, key, $"{path}.{key}");
        var size = Number("SizeReel");
        var quantity = Number("Qty");
        var reels = Number("Reel");
        var rate = Number("Rate");
        var secondary = Number("QtySDU");
        var unit = row.GetValueOrDefault("SizeUnit");
        if (unit is not ("MM" or "CM" or "INCH"))
            throw ApiFault.At(422, "invalid_line", "Select a valid size unit.", $"{path}.SizeUnit");
        foreach (var (value, key) in new[] { (size, "SizeReel"), (quantity, "Qty"), (reels, "Reel"), (rate, "Rate"), (secondary, "QtySDU") })
            if (value < 0) throw ApiFault.At(422, "invalid_line", "Enter a nonnegative value.", $"{path}.{key}");
        if (complete && size == 0)
            throw ApiFault.At(422, "invalid_line", "Width must be greater than zero.", $"{path}.SizeReel");
        var cm = unit == "MM" ? ExpenseCalculator.Round(size / 10, 3) : unit == "CM" ? size : ExpenseCalculator.Round(size * 2.54m, 3);
        var inch = unit == "INCH" ? size : ExpenseCalculator.Round(cm / 2.54m, 3);
        var basis = unit == "INCH" ? size * settings.InchKg : cm * settings.CmKg;
        // Windows derives only a zero counterpart, before applying secondary-unit conversion.
        if (inch * settings.InchKg > 0 && basis > 0)
        {
            if (reels == 0 && quantity > 0) reels = ExpenseCalculator.Round(quantity / basis, 0);
            if (quantity == 0 && reels > 0)
                quantity = decimal.Round(reels * basis, settings.WeightPlaces, MidpointRounding.AwayFromZero);
        }
        if (secondary > 0)
        {
            if (conversion <= 0)
                throw ApiFault.At(422, "invalid_conversion",
                    $"Row {rowNumber}: the item has no valid secondary-unit conversion. Choose an item with a configured conversion, or set Secondary weight to 0 and enter Weight directly.",
                    $"{path}.ItemCode", $"{path}.QtySDU");
            quantity = ExpenseCalculator.Round(secondary * conversion, 3);
        }
        if (complete && quantity <= 0)
            throw ApiFault.At(422, "quantity_required", "Enter a positive Weight or Reels quantity.", $"{path}.Qty", $"{path}.Reel");
        return new(quantity, reels, cm, inch, ExpenseCalculator.Round(quantity * rate, 2));
    }
}
