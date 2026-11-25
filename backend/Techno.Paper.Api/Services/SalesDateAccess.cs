using System.Globalization;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

internal static class SalesDateAccess
{
    public static async Task<string?> CutoffAsync(Database database, SessionResponse session, string action, CancellationToken ct)
    {
        await using var control = await database.OpenAsync(true, ct);
        var daysColumn = action switch { "view" => "VP_Days", "delete" => "DP_Days", _ => "SP_days" };
        var rows = await Database.QueryAsync(control, null,
            $"""
            SELECT CONVERT(varchar(10),DATEADD(day,0-{daysColumn},GETDATE()),23) AS Cutoff
            FROM dbo.User_Voucher_Permission WHERE User_Name=@user AND Comp_Code=@company AND V_Type='SORD' AND Category='SORD'
            """, ct, ("@user", session.Username), ("@company", session.Company));
        if (rows.Count > 1) throw new PolicyConflictException();
        return rows.Count == 0 ? null : rows[0]["Cutoff"];
    }

    public static void CheckDate(SessionResponse session, string? date, string? cutoff)
    {
        if (!DateOnly.TryParseExact(date is { Length: >= 10 } ? date[..10] : "", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var actual) ||
            actual < DateOnly.Parse(session.StartDate, CultureInfo.InvariantCulture) ||
            actual > DateOnly.Parse(session.EndDate, CultureInfo.InvariantCulture))
            throw ApiFault.At(422, "financial_date", "The order date must be within the selected financial year.", "header.V_Date");
        if (cutoff is not null && actual < DateOnly.Parse(cutoff, CultureInfo.InvariantCulture))
            throw ApiFault.At(403, "voucher_date_denied", "This order is outside the permitted voucher date window.", "header.V_Date");
    }
}
