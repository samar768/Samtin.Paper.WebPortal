using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Tests;

public sealed class ExpenseTests
{
    [Fact]
    public void Full_value_flags_preserve_confirmed_zero_bill_and_net_behavior()
    {
        List<Dictionary<string, string?>> definitions =
        [
            Definition("1", "G", "[G]", "Y"),
            Definition("2", "BAMT", "[G]", "N"),
            Definition("3", "ROFF", "ROUND([G],0)-[G]", "N"),
            Definition("4", "NAMT", "ROUND([G],0)", "N")
        ];
        var result = ExpenseCalculator.Calculate(definitions, [], 10930m);
        Assert.Equal(10930m, ExpenseCalculator.Number(result[0], "Amount"));
        Assert.All(result.Skip(1), row => Assert.Equal(0m, ExpenseCalculator.Number(row, "Amount")));
    }

    [Fact]
    public void Unsupported_sql_formulas_are_rejected() =>
        Assert.Throws<ApiFault>(() => ExpenseCalculator.Calculate([Definition("1", "G", "EXEC dbo.Anything", "Y")], [], 1));

    [Fact]
    public void Readonly_expenses_cannot_be_forged() =>
        Assert.Throws<ApiFault>(() => ExpenseCalculator.Calculate([Definition("1", "G", "[G]", "Y")],
            [new() { ["SNo"] = "1", ["Amount"] = "999" }], 1));

    [Fact]
    public void Discounts_and_expense_midpoints_match_Windows_format()
    {
        var discount = Definition("2", "DISC", "[IV]", "N");
        discount["PerEditable"] = "Y";
        var result = ExpenseCalculator.Calculate([Definition("1", "IV", "[IV]", "Y"), discount,
            Definition("3", "NET", "[IV]-[DISC]", "Y")], [new() { ["SNo"] = "2", ["Per"] = "5" }], 24.5m);
        Assert.Equal(1.23m, ExpenseCalculator.Number(result[1], "Amount"));
        Assert.Equal(23.27m, ExpenseCalculator.Number(result[2], "Amount"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Calculates_discounted_line_tax_and_rounded_header_rollup(bool complete)
    {
        var discount = Definition("2", "DISC", "[IV]", "N");
        discount["PerEditable"] = "Y";
        var tax = Definition("4", "CGST", "[BASE]", "N");
        tax["Per"] = "5";
        Map(tax, "SOrdDet", "CGSTPer", "CGSTAmt");
        List<Dictionary<string, string?>> lineDefinitions =
        [
            Definition("1", "IV", "[IV]", "Y"), discount,
            Definition("3", "BASE", "[IV]-[DISC]", "Y"), tax,
            Definition("5", "INV", "[BASE]+[CGST]", "Y")
        ];
        var lines = Enumerable.Range(0, 2).Select(_ => ExpenseCalculator.Calculate(lineDefinitions,
            [new() { ["SNo"] = "2", ["Per"] = "10" }], 10.10m, complete: complete)).ToList();
        foreach (var line in lines)
        {
            Assert.Equal(9.09m, ExpenseCalculator.Number(line[2], "Amount"));
            Assert.Equal(9.54m, ExpenseCalculator.Number(line[4], "Amount"));
            var lineValues = new Dictionary<string, object?>();
            OrderService.ApplyExpenseTargets(lineValues, [line[3]], "SOrdDet");
            Assert.Equal(5m, lineValues["CGSTPer"]);
            Assert.Equal(0.45m, lineValues["CGSTAmt"]);
        }

        var rollup = Definition("3", "CGST", "", "N");
        Map(rollup, "SOrd", null, "CGSTAmt");
        var headerOnly = Definition("4", "CESS", "[BASE]", "N");
        headerOnly["Per"] = "2";
        Map(headerOnly, "SOrd", "CESSPer", "CESSAmt");
        var net = Definition("5", "NAMT", "ROUND([BASE]+([CGST]+[CESS]), 2)", "Y");
        Map(net, "SOrd", null, "NAmount");
        List<Dictionary<string, string?>> headerDefinitions =
        [
            WithTable(Definition("1", "G", "[G]", "Y"), "SOrd"),
            WithTable(Definition("2", "BASE", "", "N"), "SOrd"), rollup, headerOnly, net
        ];
        var headerExpenses = ExpenseCalculator.CalculateHeader(headerDefinitions, [], lineDefinitions, lines, 20.20m, complete);
        var header = new Dictionary<string, object?>();
        OrderService.ApplyExpenseTargets(header, headerExpenses, "SOrd");
        Assert.Equal(0.90m, header["CGSTAmt"]); // Sum of rounded line taxes, not tax on the combined base.
        Assert.Equal(0.36m, header["CESSAmt"]);
        Assert.Equal(19.44m, header["NAmount"]);

        var empty = ExpenseCalculator.CalculateHeader(headerDefinitions, [], lineDefinitions, [], 0, complete);
        Assert.Equal(0m, ExpenseCalculator.Number(empty[2], "Amount"));
    }

    [Fact]
    public void Tax_mapping_cannot_enable_gl_posting_or_cross_map_amount_as_rate()
    {
        var tax = Definition("1", "CGST", "[G]", "Y");
        Map(tax, "SOrd", "CGSTAmt", null);
        Assert.Throws<ApiFault>(() => OrderService.ApplyExpenseTargets(new(), [tax], "SOrd"));
        Map(tax, "SOrd", null, "CGSTAmt");
        tax["DrCr"] = "C";
        Assert.Throws<ApiFault>(() => OrderService.ApplyExpenseTargets(new(), [tax], "SOrd"));
    }

    private static Dictionary<string, string?> WithTable(Dictionary<string, string?> row, string table)
    {
        row["TableName"] = table;
        return row;
    }

    private static void Map(Dictionary<string, string?> row, string table, string? per, string? amount)
    {
        row["TableName"] = table;
        row["Per_SaveIn"] = per;
        row["Amt_SaveIn"] = amount;
    }

    private static Dictionary<string, string?> Definition(string number, string code, string formula, string fullValue) => new()
    {
        ["SNo"] = number, ["ExpShortCode"] = code, ["FormulaString"] = formula, ["FullValue"] = fullValue,
        ["Per"] = "0", ["Amount"] = "0", ["PerEditable"] = "N", ["AmtEditable"] = "N", ["RoundOff"] = "N"
    };
}
