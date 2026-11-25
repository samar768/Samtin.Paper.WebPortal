using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Infrastructure;

internal static class VoucherExpenseStore
{
    public static Task<List<Dictionary<string, string?>>> LoadTemplateAsync(
        SqlConnection connection, SqlTransaction? transaction, string voucherType, bool line, CancellationToken ct) =>
        Database.QueryAsync(connection, transaction,
            $"""
            SELECT e.*,m.ExpCode AS ExpShortCode,m.Expense AS Label,CAST(0 AS decimal(18,2)) AS Amount
            FROM dbo.[{(line ? "ExpenseLine" : "ExpenseDet")}] e JOIN dbo.ExpenseMast m ON m.Code=e.ExpCode
            WHERE e.VType=@voucher ORDER BY e.SNo
            """, ct, ("@voucher", voucherType));

    public static Task<List<Dictionary<string, string?>>> LoadSnapshotAsync(
        SqlConnection connection, SqlTransaction? transaction, string documentId, bool line, CancellationToken ct) =>
        Database.QueryAsync(connection, transaction,
            $"""
            SELECT e.*,m.Expense AS Label
            FROM dbo.[{(line ? "SPExpenseLine" : "SPExpense")}] e LEFT JOIN dbo.ExpenseMast m ON m.Code=e.ExpCode
            WHERE e.DocId=@id ORDER BY {(line ? "e.V_SNo," : "")}e.SNo
            """, ct, ("@id", documentId));

    public static List<Dictionary<string, string?>> ForLine(
        List<Dictionary<string, string?>> stored, string lineNumber) =>
        stored.Where(row => row["V_SNo"] == lineNumber).ToList();

    public static async Task<List<ItemExpenseDefault>> LoadItemDefaultsAsync(
        SqlConnection connection, SqlTransaction? transaction, string item, string site, CancellationToken ct)
    {
        var rows = await Database.QueryAsync(connection, transaction,
            """
            SELECT d.Code AS BandCode,d.ExpCode,d.Per,d.Amount
            FROM dbo.ItemMast i LEFT JOIN dbo.TaxBandDet d ON d.Code=i.TaxBandCode
            WHERE i.Code=@item AND i.ItemType='FM'
              AND (NULLIF(i.SiteList,'') IS NULL OR CHARINDEX('|' + @site + '|','|' + i.SiteList + '|')>0)
            ORDER BY d.ExpCode
            """, ct, ("@item", item), ("@site", site));
        if (rows.Count == 0) throw new ApiFault(422, "invalid_lookup", "Select a finished item available in this workspace.");
        if (rows.Count == 1 && rows[0]["BandCode"] is null) return [];
        if (rows.Any(row => string.IsNullOrWhiteSpace(row["ExpCode"])) ||
            rows.GroupBy(row => row["ExpCode"]!.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ApiFault(422, "invalid_tax_band", "The item tax band has missing or duplicate expense codes.");
        return rows.Select(row => new ItemExpenseDefault(row["ExpCode"]!.Trim(),
            ExpenseCalculator.Text(ExpenseCalculator.Round(ExpenseCalculator.Number(row, "Per"), 3)),
            ExpenseCalculator.Text(ExpenseCalculator.Round(ExpenseCalculator.Number(row, "Amount"), 2)))).ToList();
    }

    public static List<Dictionary<string, string?>> ApplyItemDefaults(
        List<Dictionary<string, string?>> definitions, List<ItemExpenseDefault> defaults)
    {
        var byHead = defaults.ToDictionary(row => row.ExpCode, StringComparer.OrdinalIgnoreCase);
        return definitions.Select(row => row.GetValueOrDefault("ExpCode")?.Trim() is { } code && byHead.TryGetValue(code, out var item)
            ? new Dictionary<string, string?>(row, StringComparer.OrdinalIgnoreCase) { ["Per"] = item.Per, ["Amount"] = item.Amount }
            : row).ToList();
    }
}
