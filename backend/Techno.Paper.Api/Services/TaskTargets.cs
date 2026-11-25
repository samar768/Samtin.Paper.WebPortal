using System.Globalization;
using Techno.Paper.Api.Infrastructure;

namespace Techno.Paper.Api.Services;

public sealed record TaskTargetFilter(string Predicate, (string Name, object? Value)[] Parameters);

public interface ITaskTarget
{
    string EntityType { get; }
    string Href(string id);
    // Predicates use aliases t/k and common workspace parameters; adapter-specific parameters must be uniquely named.
    Task<TaskTargetFilter> VisibilityAsync(SessionResponse session, CancellationToken ct);
}

public sealed class SalesOrderTaskTarget(Database database) : ITaskTarget
{
    public string EntityType => "SalesOrder";
    public string Href(string id) => $"/dashboard/sale-order/{Uri.EscapeDataString(id)}";
    public async Task<TaskTargetFilter> VisibilityAsync(SessionResponse session, CancellationToken ct)
    {
        var cutoff = session.Capabilities.View ? await SalesDateAccess.CutoffAsync(database, session, "view", ct) : null;
        return new("""
            (k.EntityType='SalesOrder' AND @sordView=1 AND EXISTS(
              SELECT 1 FROM dbo.SOrd s WHERE s.DocId=t.SourceDocId AND s.Site_Code=@site
              AND s.V_Prefix=@prefix AND s.V_Type='SORD' AND s.V_Date>=@start
              AND s.V_Date<DATEADD(day,1,@end) AND (@sordCutoff IS NULL OR s.V_Date>=@sordCutoff)))
            """, [("@sordView", session.Capabilities.View), ("@sordCutoff",
                cutoff is null ? null : DateOnly.Parse(cutoff, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue))]);
    }
}
