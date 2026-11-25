using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Domain;

public static class ListingPolicy
{
    private static readonly string[] ColumnIds = ["reference", "date", "buyer", "gross", "state"];

    public static ListingPreferences Defaults() => new(1,
        [new("reference", true, 140), new("date", true, 130), new("buyer", true, 300),
            new("gross", true, 160), new("state", true, 130)], "compact", 25, "date", "desc");

    public static void ValidatePageSize(int pageSize)
    {
        if (pageSize is not (10 or 25 or 50 or 100))
            throw new ApiFault(400, "invalid_page_size", "Choose 10, 25, 50 or 100 rows per page.");
    }

    public static string OrderBy(string column, string direction)
    {
        var expression = column switch
        {
            "reference" => "s.RecId",
            "date" => "s.V_Date",
            "buyer" => "p.Name",
            "gross" => "s.GAmount",
            "state" => "CASE WHEN NULLIF(s.ApprovedBy,'') IS NULL THEN 0 ELSE 1 END",
            _ => throw new ApiFault(400, "invalid_sort", "Choose an available listing sort column.")
        };
        var sqlDirection = direction switch
        {
            "asc" => "ASC", "desc" => "DESC",
            _ => throw new ApiFault(400, "invalid_sort", "Choose ascending or descending sorting.")
        };
        return $"{expression} {sqlDirection},s.DocId DESC";
    }

    public static void Validate(ListingPreferences settings)
    {
        if (settings.SchemaVersion != 1 || settings.Columns is null ||
            settings.Columns.Count != ColumnIds.Length ||
            settings.Columns.Any(c => c is null || !ColumnIds.Contains(c.Id) || c.Width is < 80 or > 600) ||
            settings.Columns.Select(c => c.Id).Distinct().Count() != ColumnIds.Length ||
            !settings.Columns.Any(c => c.Visible) || settings.Density is not ("compact" or "comfortable"))
            throw new ApiFault(400, "invalid_listing_settings",
                "Choose at least one column, unique available columns, widths from 80 to 600 pixels and a valid row spacing.");
        ValidatePageSize(settings.PageSize);
        _ = OrderBy(settings.SortColumn, settings.SortDirection);
    }
}
