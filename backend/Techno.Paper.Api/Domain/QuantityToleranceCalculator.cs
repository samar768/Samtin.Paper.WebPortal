using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Domain;

public static class QuantityToleranceCalculator
{
    public static ToleranceResponse Calculate(ToleranceRequest request, string path = "tolerance")
    {
        try { return CalculateValues(request, path); }
        catch (OverflowException)
        {
            throw ApiFault.At(422, "invalid_tolerance", "The tolerance calculation exceeds the supported decimal range.",
                $"{path}.Excess", $"{path}.Shortage");
        }
    }

    private static ToleranceResponse CalculateValues(ToleranceRequest request, string path)
    {
        var row = new Dictionary<string, string?> {
            ["Quantity"] = request.Quantity, ["Excess"] = request.Excess, ["Shortage"] = request.Shortage,
            ["ExcessPercent"] = request.ExcessPercent, ["ShortagePercent"] = request.ShortagePercent
        };
        decimal Number(string key)
        {
            var number = ExpenseCalculator.Number(row, key, $"{path}.{key}");
            if (number < 0)
                throw ApiFault.At(422, "invalid_tolerance", "Enter a nonnegative quantity or percentage.", $"{path}.{key}");
            return number;
        }
        var quantity = Number("Quantity");
        var excess = Number("Excess");
        var shortage = Number("Shortage");
        var excessPercent = Number("ExcessPercent");
        var shortagePercent = Number("ShortagePercent");
        if (request.ExcessPercent is not null) excess = ExpenseCalculator.Round(quantity * excessPercent / 100, 3);
        if (request.ShortagePercent is not null) shortage = ExpenseCalculator.Round(quantity * shortagePercent / 100, 3);
        if (shortage > quantity)
            throw ApiFault.At(422, "invalid_tolerance", "Shortage cannot exceed the order quantity.",
                $"{path}.Shortage", $"{path}.ShortagePercent");
        return new(ExpenseCalculator.Text(excess), ExpenseCalculator.Text(shortage),
            ExpenseCalculator.Text(quantity + excess), ExpenseCalculator.Text(quantity - shortage));
    }

    public static void Validate(decimal quantity, Dictionary<string, string?> row, string path)
    {
        try
        {
            Calculate(new(ExpenseCalculator.Text(quantity), row.GetValueOrDefault("Tl_ExcessQty"),
                row.GetValueOrDefault("TL_ShortageQty"), null, null));
        }
        catch (ApiFault fault) when (fault.Status == 422)
        {
            var fields = fault.Errors?.Keys.Select(key => key switch
            {
                "tolerance.Quantity" => $"{path}.Qty",
                "tolerance.Excess" or "tolerance.ExcessPercent" => $"{path}.Tl_ExcessQty",
                _ => $"{path}.TL_ShortageQty"
            }).Distinct().ToArray() ?? [$"{path}.Tl_ExcessQty", $"{path}.TL_ShortageQty"];
            throw ApiFault.At(fault.Status, fault.Code, fault.Message, fields);
        }
    }
}
