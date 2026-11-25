using System.Globalization;
using System.Text.RegularExpressions;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Domain;

public static class ExpenseCalculator
{
    public static decimal Number(Dictionary<string, string?> row, string key, string? path = null) =>
        row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var number) ? number
                : throw (path is null ? new ApiFault(422, "invalid_number", $"{key} must be a decimal number.")
                    : ApiFault.At(422, "invalid_number", $"{key} must be a decimal number.", path))
            : 0;

    public static string Text(decimal number) => number.ToString(CultureInfo.InvariantCulture);
    public static decimal Round(decimal number, int places) => decimal.Round(number, places, MidpointRounding.AwayFromZero);

    public static List<Dictionary<string, string?>> Calculate(
        List<Dictionary<string, string?>> definitions, List<Dictionary<string, string?>> inputs, decimal gross, string? path = null,
        bool complete = true, IReadOnlyDictionary<string, decimal>? lineTotals = null)
    {
        var results = new List<Dictionary<string, string?>>();
        var tokens = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["G"] = gross, ["IV"] = gross };
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var heads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inputs.Select(r => r.GetValueOrDefault("SNo")).Distinct().Count() != inputs.Count ||
            inputs.Any(r => !definitions.Any(d => d["SNo"] == r.GetValueOrDefault("SNo")) ||
                r.Keys.Any(k => k is not ("SNo" or "Per" or "Amount"))))
            throw new ApiFault(422, "invalid_expense", "Unknown or duplicate expense input.");
        foreach (var (definition, index) in definitions.Select((definition, index) => (definition, index)))
        {
            var row = new Dictionary<string, string?>(definition, StringComparer.OrdinalIgnoreCase);
            var head = (row.GetValueOrDefault("ExpShortCode") ?? "").Trim().ToUpperInvariant();
            if (head.Length == 0 || !heads.Add(head))
                throw new ApiFault(422, "invalid_expense", "Expense head codes must be nonempty and unique.");
            var input = inputs.SingleOrDefault(i => i.GetValueOrDefault("SNo") == row["SNo"]) ?? [];
            var per = input.ContainsKey("Per") ? Number(input, "Per", path is null ? null : $"{path}[{index}].Per") : Number(row, "Per");
            var amount = input.ContainsKey("Amount") ? Number(input, "Amount", path is null ? null : $"{path}[{index}].Amount") : Number(row, "Amount");
            if (input.ContainsKey("Per") && row.GetValueOrDefault("PerEditable") != "Y" && per != Number(row, "Per") ||
                input.ContainsKey("Amount") && row.GetValueOrDefault("AmtEditable") != "Y" && amount != Number(row, "Amount"))
                throw new ApiFault(403, "expense_readonly", "This expense input is read-only.");
            if (lineTotals is not null && lineTotals.TryGetValue(head, out var total))
            {
                amount = total;
                if (head is not ("G" or "IV") || index != 0) tokens[head] = total;
            }
            var formula = row.GetValueOrDefault("FormulaString") ?? "";
            var value = Evaluate(formula, tokens, rates, head);
            if (row.GetValueOrDefault("FullValue") == "Y") amount = value;
            else if (per > 0) amount = value * per / 100;
            amount = row.GetValueOrDefault("RoundOff") switch
            {
                "D" => Round(amount, 6),
                "Y" => Round(amount, 0),
                "H" => decimal.Ceiling(amount),
                "L" => decimal.Floor(amount),
                _ => Round(amount, 2)
            };
            if (complete && (row.GetValueOrDefault("MandatoryPer") == "Y" && per == 0 ||
                row.GetValueOrDefault("MandatoryAmt") == "Y" && amount == 0))
                throw path is null ? new ApiFault(422, "expense_required", "A mandatory configured expense is missing.")
                    : ApiFault.At(422, "expense_required", "A mandatory configured expense is missing.",
                        $"{path}[{index}].{(row.GetValueOrDefault("MandatoryPer") == "Y" && per == 0 ? "Per" : "Amount")}");
            row["Per"] = Text(per);
            row["Amount"] = Text(amount);
            tokens[head] = amount;
            rates[head] = per;
            results.Add(row);
        }
        return results;
    }

    public static List<Dictionary<string, string?>> CalculateHeader(
        List<Dictionary<string, string?>> definitions, List<Dictionary<string, string?>> inputs,
        IEnumerable<Dictionary<string, string?>> lineDefinitions, IEnumerable<List<Dictionary<string, string?>>> lines,
        decimal gross, bool complete)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in lineDefinitions)
            totals.TryAdd(row["ExpShortCode"]!.Trim(), 0);
        foreach (var row in lines.SelectMany(line => line))
        {
            var head = row["ExpShortCode"]!.Trim();
            totals[head] = totals.GetValueOrDefault(head) + Number(row, "Amount");
        }
        return Calculate(definitions, inputs, totals.GetValueOrDefault("IV", gross), "headerExpenses", complete, totals);
    }

    private static readonly Regex FormulaToken = new(
        @"\G\s*(\[[A-Z0-9_ -]+\]|\{[A-Z0-9_ -]+\}|ROUND|[0-9]+(?:\.[0-9]+)?|[()+,\-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static decimal Evaluate(string formula, Dictionary<string, decimal> amounts, Dictionary<string, decimal> rates, string head)
    {
        formula = formula.Trim();
        if (formula.Length == 0) return 0;
        var position = 0;
        ApiFault Invalid() => new(422, "unsupported_formula", $"Expense {head} has an unsupported formula or an unavailable expense reference.");
        if (formula.Length > 250) throw Invalid();

        string? Next()
        {
            if (position == formula.Length) return null;
            var match = FormulaToken.Match(formula, position);
            if (!match.Success) throw Invalid();
            position += match.Length;
            return match.Groups[1].Value;
        }

        var token = Next();
        void Expect(string expected)
        {
            if (token != expected) throw Invalid();
            token = Next();
        }

        decimal Expression()
        {
            var result = Term();
            while (token is "+" or "-")
            {
                var operation = token;
                token = Next();
                var term = Term();
                result = operation == "+" ? result + term : result - term;
            }
            return result;
        }

        decimal Term()
        {
            var current = token ?? throw Invalid();
            token = Next();
            if (current is "+" or "-") return current == "+" ? Term() : -Term();
            if (current == "(")
            {
                var result = Expression();
                Expect(")");
                return result;
            }
            if (current.Equals("ROUND", StringComparison.OrdinalIgnoreCase))
            {
                Expect("(");
                var result = Expression();
                Expect(",");
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var places) || places > 28)
                    throw Invalid();
                token = Next();
                Expect(")");
                return Round(result, places);
            }
            if (current[0] is '[' or '{')
                return (current[0] == '[' ? amounts : rates).TryGetValue(current[1..^1].Trim(), out var value)
                    ? value : throw Invalid();
            return decimal.TryParse(current, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var literal)
                ? literal : throw Invalid();
        }

        try
        {
            var result = Expression();
            if (token is not null) throw Invalid();
            return result;
        }
        catch (OverflowException) { throw Invalid(); }
    }
}
