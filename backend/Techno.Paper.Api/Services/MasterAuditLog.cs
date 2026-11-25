using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

internal static class MasterAuditLog
{
    public static Task<int> WriteAsync(SqlConnection connection, SqlTransaction transaction, MasterDefinitionResponse definition,
        SessionResponse session, string recordId, string operation, Dictionary<string, string?>? before,
        Dictionary<string, string?>? after, CancellationToken ct) => Database.ExecuteAsync(connection, transaction,
        """
        INSERT INTO webpoc.MasterAudit(MasterId,RecordId,Username,Operation,BeforeJson,AfterJson,
            CompanyCode,SiteCode,MetadataRevision,ValuesCaptured)
        VALUES(@master,@key,@user,@operation,@before,@after,@company,@site,@revision,@captured)
        """, ct, ("@master", definition.Id), ("@key", recordId), ("@user", session.Username),
        ("@operation", operation), ("@before", definition.CaptureAuditValues && before is not null ? JsonSerializer.Serialize(before) : null),
        ("@after", definition.CaptureAuditValues && after is not null ? JsonSerializer.Serialize(after) : null),
        ("@company", session.Company), ("@site", session.Site), ("@revision", definition.Revision), ("@captured", definition.CaptureAuditValues));

    public static async Task<MasterAuditResponse> ReadAsync(SqlConnection connection, MasterDefinitionResponse definition,
        SessionResponse session, string? recordId, int page, CancellationToken ct)
    {
        if (!definition.Capabilities.Audit) throw new ApiFault(403, "master_denied", "Audit history is not permitted.");
        if (page is < 0 or > 10000) throw new ApiFault(400, "invalid_audit_page", "Invalid audit page.");
        var rows = await Database.QueryAsync(connection, null,
            """
            WITH Entries AS (SELECT *,ROW_NUMBER() OVER(ORDER BY Id DESC) AS RowNumber FROM webpoc.MasterAudit
              WHERE MasterId=@master AND (@record IS NULL OR RecordId=@record) AND (@global=1 OR SiteCode=@site))
            SELECT * FROM Entries WHERE RowNumber>@skip AND RowNumber<=@skip+26 ORDER BY RowNumber
            """, ct, ("@master", definition.Id), ("@record", recordId), ("@global", definition.Scope == "global"),
            ("@site", session.Site), ("@skip", page * 25));
        Dictionary<string, string?>? Values(string? json) => json is null ? null :
            JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ??
                throw new ApiFault(409, "master_configuration", "Invalid audit snapshot requires administrator review.");
        var entries = rows.Take(25).Select(row => new MasterAuditEntryResponse(row["Id"]!, row["RecordId"]!,
            row["Username"]!, row["Operation"]!, DateTime.Parse(row["OccurredAt"]!, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToString("O", CultureInfo.InvariantCulture),
            row["CompanyCode"], row["SiteCode"], row["MetadataRevision"] is null ? null : int.Parse(row["MetadataRevision"]!, CultureInfo.InvariantCulture),
            row["ValuesCaptured"] == "True", Values(row["BeforeJson"]), Values(row["AfterJson"]))).ToList();
        return new(definition, entries, page, rows.Count > 25);
    }
}
