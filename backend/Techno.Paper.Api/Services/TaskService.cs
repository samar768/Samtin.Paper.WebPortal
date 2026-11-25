using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

public interface ITaskService
{
    Task<TaskInboxResponse> ListAsync(SessionResponse session, int page, CancellationToken ct);
    Task<ErpTaskResponse> GetAsync(SessionResponse session, Guid id, CancellationToken ct);
    Task ReadAsync(SessionResponse session, Guid id, CancellationToken ct);
    Task<IReadOnlyList<PublishableTaskTypeResponse>> TypesAsync(SessionResponse session, CancellationToken ct);
    Task<ErpTaskResponse> PublishAsync(SessionResponse session, PublishTaskRequest request, CancellationToken ct);
    Task SubmitDocumentAsync(SqlConnection connection, SqlTransaction transaction, SessionResponse session,
        string voucherType, string id, string reference, string version, string creator, string eventKey, bool created, CancellationToken ct);
    Task CancelDocumentAsync(SqlConnection connection, SqlTransaction transaction, SessionResponse session, string id, CancellationToken ct);
    Task<ApprovalStateResponse> ApprovalAsync(SqlConnection connection, SqlTransaction? transaction,
        SessionResponse session, string id, bool approvalPermission, CancellationToken ct);
    Task<bool> DecideAsync(SqlConnection connection, SqlTransaction transaction, SessionResponse session,
        string id, string sourceVersion, OrderDecisionRequest decision, bool approvalPermission, CancellationToken ct);
    Task<bool> CanReverseAsync(SqlConnection connection, SqlTransaction? transaction, SessionResponse session, string voucherType, CancellationToken ct);
    Task ReverseDocumentAsync(SqlConnection connection, SqlTransaction transaction, SessionResponse session,
        string voucherType, string id, string reference, string creator, string previousApprover, UnapproveOrderRequest request, CancellationToken ct);
}

public sealed class TaskService(Database database, PocOptions options, IEnumerable<ITaskTarget> targets) : ITaskService
{
    private readonly Dictionary<string, ITaskTarget> targetMap = targets.ToDictionary(target => target.EntityType, StringComparer.Ordinal);
    private const string Scope = "Company=@company AND Prefix=@prefix AND Site=@site";
    private const string Visible = """
        FROM webpoc.erpTaskInstance t JOIN webpoc.erpTaskType k ON k.Code=t.TypeCode
        LEFT JOIN webpoc.erpTaskReceipt r ON r.TaskId=t.Id AND r.Username=@user
        WHERE t.Company=@company AND t.Prefix=@prefix AND t.Site=@site
          AND EXISTS(SELECT 1 FROM webpoc.erpTaskAudience a WHERE a.TaskId=t.Id AND
            (a.Username=@user OR EXISTS(SELECT 1 FROM webpoc.erpTaskGroupMember m
             JOIN webpoc.erpTaskGroup g ON g.Code=m.GroupCode AND g.Active=1
             JOIN webpoc.erpTaskTypeGroup p ON p.GroupCode=g.Code AND p.TypeCode=t.TypeCode
             WHERE m.Username=@user AND m.GroupCode=a.GroupCode AND g.Company=t.Company AND g.Site=t.Site)))
        """;
    private static (string Name, object? Value)[] Context(SessionResponse s) =>
        [("@company", s.Company), ("@prefix", s.Prefix), ("@site", s.Site), ("@user", s.Username)];
    private static DateTime Date(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
    private static DateTimeOffset Stamp(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private ErpTaskResponse Map(Dictionary<string, string?> r) => new(Guid.Parse(r["Id"]!), r["TypeCode"]!,
        r["Category"]!, r["Title"]!, r["Body"]!, r["State"]!, Stamp(r["CreatedAt"]!), r["CreatedBy"]!, r["IsRead"] == "1",
        r["OpenMode"] == "Transaction" && r["EntityType"] is { } entity && targetMap.TryGetValue(entity, out var target) && r["SourceDocId"] is { } id
            ? target.Href(id) : null);
    private void Writable(SessionResponse session)
    {
        if (session.ReadOnly || !options.DevelopmentWritesApproved)
            throw new ApiFault(403, "task_read_only", "Publishing or deciding tasks is not permitted in this workspace.");
    }

    private async Task<TaskTargetFilter> VisibilityAsync(SessionResponse s, CancellationToken ct)
    {
        var filters = await Task.WhenAll(targetMap.Values.Select(target => target.VisibilityAsync(s, ct)));
        var predicate = filters.Length == 0 ? "1=0" : string.Join(" OR ", filters.Select(filter => filter.Predicate));
        return new($"{Visible} AND (k.EntityType IS NULL OR ({predicate}))",
            [.. Context(s), ("@start", Date(s.StartDate)), ("@end", Date(s.EndDate)), .. filters.SelectMany(filter => filter.Parameters)]);
    }

    public async Task<TaskInboxResponse> ListAsync(SessionResponse session, int page, CancellationToken ct)
    {
        if (page is < 0 or > 10000) throw new ApiFault(400, "task_page", "Invalid task page.");
        var filter = await VisibilityAsync(session, ct);
        await using var connection = await database.OpenAsync(false, ct);
        var rows = await Database.QueryAsync(connection, null, $"""
            WITH inbox AS (SELECT t.*,k.Category,k.OpenMode,k.EntityType,
              CASE WHEN r.TaskId IS NULL THEN 0 ELSE 1 END AS IsRead,
              ROW_NUMBER() OVER(ORDER BY t.CreatedAt DESC,t.Id DESC) AS RowNumber {filter.Predicate})
            SELECT * FROM inbox WHERE RowNumber BETWEEN @offset+1 AND @offset+26 ORDER BY RowNumber
            """, ct, [.. filter.Parameters, ("@offset", page * 25)]);
        var count = await Database.QueryAsync(connection, null,
            $"SELECT COUNT(*) AS Unread {filter.Predicate} AND r.TaskId IS NULL AND t.State='Open'", ct, filter.Parameters);
        return new(rows.Take(25).Select(Map).ToArray(), int.Parse(count[0]["Unread"]!, CultureInfo.InvariantCulture), rows.Count > 25);
    }

    public async Task<ErpTaskResponse> GetAsync(SessionResponse session, Guid id, CancellationToken ct)
    {
        var filter = await VisibilityAsync(session, ct);
        await using var connection = await database.OpenAsync(false, ct);
        var rows = await Database.QueryAsync(connection, null,
            $"SELECT t.*,k.Category,k.OpenMode,k.EntityType,CASE WHEN r.TaskId IS NULL THEN 0 ELSE 1 END AS IsRead {filter.Predicate} AND t.Id=@id",
            ct, [.. filter.Parameters, ("@id", id.ToString())]);
        return rows.Count == 1 ? Map(rows[0]) : throw new ApiFault(404, "task_not_found", "Task not found in your authorized workspace.");
    }

    public async Task ReadAsync(SessionResponse session, Guid id, CancellationToken ct)
    {
        _ = await GetAsync(session, id, ct);
        if (!options.DevelopmentWritesApproved) throw new ApiFault(403, "writes_not_approved", "Development writes are disabled.");
        await using var connection = await database.OpenAsync(false, ct);
        await Database.ExecuteAsync(connection, null, """
            SET XACT_ABORT ON; BEGIN TRAN;
            IF NOT EXISTS(SELECT 1 FROM webpoc.erpTaskReceipt WITH(UPDLOCK,HOLDLOCK) WHERE TaskId=@id AND Username=@user)
              INSERT webpoc.erpTaskReceipt(TaskId,Username) VALUES(@id,@user);
            COMMIT;
            """, ct, ("@id", id.ToString()), ("@user", session.Username));
    }

    public async Task<IReadOnlyList<PublishableTaskTypeResponse>> TypesAsync(SessionResponse session, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(false, ct);
        var rows = await Database.QueryAsync(connection, null, """
            SELECT t.Code,t.Name FROM webpoc.erpTaskType t WHERE t.ManualPublish=1 AND t.OpenMode='Dialog' AND t.EntityType IS NULL
            AND EXISTS(SELECT 1 FROM webpoc.erpTaskTypeGroup p
              JOIN webpoc.erpTaskGroup g ON g.Code=p.GroupCode AND g.Active=1
              JOIN webpoc.erpTaskGroupMember m ON m.GroupCode=g.Code
              WHERE p.TypeCode=t.Code AND p.CanPublish=1 AND g.Company=@company AND g.Site=@site AND m.Username=@user)
            ORDER BY t.Name
            """, ct, Context(session));
        return session.ReadOnly ? [] : rows.Select(r => new PublishableTaskTypeResponse(r["Code"]!, r["Name"]!)).ToArray();
    }

    private static async Task<Guid> InsertAsync(SqlConnection c, SqlTransaction tx, SessionResponse s,
        string type, string title, string body, string key, string? doc, Guid? request, int? step,
        string? group, string? recipient, CancellationToken ct)
    {
        var rows = await Database.QueryAsync(c, tx,
            $"SELECT Id FROM webpoc.erpTaskInstance WITH(UPDLOCK,HOLDLOCK) WHERE {Scope} AND EventKey=@key",
            ct, [.. Context(s), ("@key", key)]);
        if (rows.Count > 0) return Guid.Parse(rows[0]["Id"]!);
        var id = Guid.NewGuid();
        await Database.ExecuteAsync(c, tx, """
            INSERT webpoc.erpTaskInstance(Id,Company,Prefix,Site,TypeCode,Title,Body,State,SourceDocId,RequestId,Step,EventKey,CreatedBy)
            VALUES(@id,@company,@prefix,@site,@type,@title,@body,'Open',@doc,@request,@step,@key,@user);
            INSERT webpoc.erpTaskHistory(TaskId,Actor,Action) VALUES(@id,@user,'Published');
            """, ct, [.. Context(s), ("@id", id.ToString()), ("@type", type), ("@title", title), ("@body", body),
                ("@doc", doc), ("@request", request?.ToString()), ("@step", step), ("@key", key)]);
        if (group is not null)
            await Database.ExecuteAsync(c, tx, "INSERT webpoc.erpTaskAudience(TaskId,GroupCode) VALUES(@id,@group)",
                ct, ("@id", id.ToString()), ("@group", group));
        else
            await Database.ExecuteAsync(c, tx, """
                INSERT webpoc.erpTaskAudience(TaskId,GroupCode)
                SELECT @id,p.GroupCode FROM webpoc.erpTaskTypeGroup p JOIN webpoc.erpTaskGroup g ON g.Code=p.GroupCode
                WHERE p.TypeCode=@type AND g.Active=1 AND g.Company=@company AND g.Site=@site
                """, ct, [.. Context(s), ("@id", id.ToString()), ("@type", type)]);
        if (recipient is not null)
            await Database.ExecuteAsync(c, tx, "INSERT webpoc.erpTaskAudience(TaskId,Username) VALUES(@id,@recipient)",
                ct, ("@id", id.ToString()), ("@recipient", recipient));
        return id;
    }

    public async Task<ErpTaskResponse> PublishAsync(SessionResponse session, PublishTaskRequest request, CancellationToken ct)
    {
        Writable(session);
        var title = request.Title.Trim(); var body = request.Body.Trim();
        if (title.Length is < 1 or > 200 || body.Length is < 1 or > 2000 || request.IdempotencyKey == Guid.Empty)
            throw new ApiFault(422, "invalid_task", "Provide a title (1-200), message (1-2000), and non-empty idempotency key.");
        if (!(await TypesAsync(session, ct)).Any(t => t.Code == request.Type))
            throw new ApiFault(403, "task_publish_denied", "This task type is not permitted for manual publication.");
        await using var c = await database.OpenAsync(false, ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var key = $"manual:{session.Username}:{request.IdempotencyKey}";
        var prior = await Database.QueryAsync(c, tx,
            $"SELECT TypeCode,Title,Body FROM webpoc.erpTaskInstance WITH(UPDLOCK,HOLDLOCK) WHERE {Scope} AND EventKey=@key",
            ct, [.. Context(session), ("@key", key)]);
        if (prior.Count > 0 && (prior[0]["TypeCode"] != request.Type || prior[0]["Title"] != title || prior[0]["Body"] != body))
            throw new ApiFault(409, "task_idempotency", "This publication key was already used with different content.");
        var id = await InsertAsync(c, tx, session, request.Type, title, body, key, null, null, null, null, session.Username, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(session, id, ct);
    }

    public async Task CancelDocumentAsync(SqlConnection c, SqlTransaction tx, SessionResponse s, string id, CancellationToken ct)
    {
        Writable(s);
        await Database.ExecuteAsync(c, tx, $"""
            INSERT webpoc.erpTaskHistory(TaskId,Actor,Action)
            SELECT Id,@user,'SupersededOrDeleted' FROM webpoc.erpTaskInstance
             WHERE {Scope} AND SourceDocId=@doc AND State='Open';
            UPDATE webpoc.erpTaskInstance SET State='Cancelled' WHERE {Scope} AND SourceDocId=@doc AND State='Open';
            UPDATE st SET State='Cancelled' FROM webpoc.erpApprovalRequestStage st
             JOIN webpoc.erpApprovalRequest r ON r.Id=st.RequestId
             WHERE r.Company=@company AND r.Site=@site AND r.Prefix=@prefix AND r.DocId=@doc AND r.State='Pending'
               AND st.State IN ('Active','Waiting');
            UPDATE webpoc.erpApprovalRequest SET State='Cancelled' WHERE {Scope} AND DocId=@doc AND State='Pending';
            """, ct, [.. Context(s), ("@doc", id)]);
    }

    public async Task SubmitDocumentAsync(SqlConnection c, SqlTransaction tx, SessionResponse s, string voucherType, string id,
        string reference, string version, string creator, string eventKey, bool created, CancellationToken ct)
    {
        Writable(s);
        var flow = await Database.QueryAsync(c, tx, """
            SELECT f.Id,f.AllowSelf,f.AllowRepeat,f.ApprovalType,f.CreatedType,f.ResultType,a.EntityType FROM webpoc.erpApprovalFlow f
            JOIN webpoc.erpTaskType a ON a.Code=f.ApprovalType AND a.Category='Approval' AND a.OpenMode='Transaction'
            JOIN webpoc.erpTaskType n ON n.Code=f.CreatedType AND n.Category='Notification' AND n.OpenMode='Transaction' AND n.EntityType=a.EntityType
            JOIN webpoc.erpTaskType r ON r.Code=f.ResultType AND r.Category='Notification' AND r.OpenMode='Transaction' AND r.EntityType=a.EntityType
            WHERE f.Company=@company AND f.Site=@site AND f.VoucherType=@voucher AND f.Enabled=1
            """, ct, [.. Context(s), ("@voucher", voucherType)]);
        if (flow.Count != 1) throw new ApiFault(409, "approval_configuration", "Configure an enabled voucher approval workflow for this company/site.");
        if (flow[0]["EntityType"] is not { } entity || !targetMap.ContainsKey(entity))
            throw new ApiFault(409, "task_target_configuration", "Register this voucher's transaction task adapter before publishing.");
        var stages = await Database.QueryAsync(c, tx, """
            SELECT st.Step,st.Name,st.GroupCode,
              CASE WHEN EXISTS(SELECT 1 FROM webpoc.erpTaskGroup g
                JOIN webpoc.erpTaskTypeGroup p ON p.GroupCode=g.Code AND p.TypeCode=@type AND p.CanAct=1
                WHERE g.Code=st.GroupCode AND g.Active=1 AND g.Company=@company AND g.Site=@site
                  AND EXISTS(SELECT 1 FROM webpoc.erpTaskGroupMember m WHERE m.GroupCode=g.Code))
              THEN 1 ELSE 0 END AS Eligible
            FROM webpoc.erpApprovalFlowStage st WHERE st.FlowId=@flow ORDER BY st.Step
            """, ct, [.. Context(s), ("@flow", flow[0]["Id"]), ("@type", flow[0]["ApprovalType"])]);
        if (stages.Count is < 1 or > 20 || stages.Where((stage, index) => stage["Step"] != (index + 1).ToString(CultureInfo.InvariantCulture)
                || stage["Eligible"] != "1").Any())
            throw new ApiFault(409, "approval_configuration", "Approval stages must be contiguous and have active, scoped recipient groups with action permission.");
        await CancelDocumentAsync(c, tx, s, id, ct);
        var request = Guid.NewGuid();
        await Database.ExecuteAsync(c, tx, """
            INSERT webpoc.erpApprovalRequest(Id,Company,Prefix,Site,VoucherType,DocId,SourceVersion,FlowId,CreatedBy,State,AllowSelf,AllowRepeat,ApprovalType,ResultType)
            VALUES(@id,@company,@prefix,@site,@voucher,@doc,@version,@flow,@creator,'Pending',@self,@repeat,@type,@resultType);
            INSERT webpoc.erpApprovalRequestStage(RequestId,Step,Name,GroupCode,State)
            SELECT @id,Step,Name,GroupCode,CASE WHEN Step=1 THEN 'Active' ELSE 'Waiting' END
             FROM webpoc.erpApprovalFlowStage WHERE FlowId=@flow;
            """, ct, [.. Context(s), ("@id", request.ToString()), ("@doc", id), ("@version", version), ("@flow", flow[0]["Id"]),
                ("@creator", creator), ("@self", flow[0]["AllowSelf"] == "True"), ("@repeat", flow[0]["AllowRepeat"] == "True"),
                ("@voucher", voucherType), ("@type", flow[0]["ApprovalType"]), ("@resultType", flow[0]["ResultType"])]);
        await InsertAsync(c, tx, s, flow[0]["ApprovalType"]!, $"Document {reference}: {stages[0]["Name"]}",
            $"Review {voucherType} {reference}. Approval stage 1 of {stages.Count}.", $"approval:{request}:1",
            id, request, 1, stages[0]["GroupCode"], null, ct);
        if (created)
            await InsertAsync(c, tx, s, flow[0]["CreatedType"]!, $"{voucherType} {reference} created",
                "A new document is ready for its configured approval workflow.", $"created:{eventKey}", id, null, null, null, creator, ct);
    }

    public async Task<ApprovalStateResponse> ApprovalAsync(SqlConnection c, SqlTransaction? tx, SessionResponse s, string id, bool approvalPermission, CancellationToken ct)
    {
        var rows = await Database.QueryAsync(c, tx,
            $"SELECT TOP(1) * FROM webpoc.erpApprovalRequest WHERE {Scope} AND DocId=@doc ORDER BY Revision DESC",
            ct, [.. Context(s), ("@doc", id)]);
        var reversals = await Database.QueryAsync(c, tx,
            $"SELECT TOP(1) RequestId,Actor,Reason FROM webpoc.erpApprovalReversal WHERE {Scope} AND DocId=@doc ORDER BY CreatedAt DESC,Id DESC",
            ct, [.. Context(s), ("@doc", id)]);
        var reversed = reversals.FirstOrDefault(r => r["RequestId"] == (rows.Count == 0 ? null : rows[0]["Id"]));
        if (rows.Count == 0) return new(null, reversed is null ? "NotRequested" : "Reversed", null, false, [],
            ReversedBy: reversed?.GetValueOrDefault("Actor"), ReversalReason: reversed?.GetValueOrDefault("Reason"));
        var request = rows[0];
        var stages = await Database.QueryAsync(c, tx, """
            SELECT st.*,g.Name AS GroupName,CASE WHEN EXISTS(
              SELECT 1 FROM webpoc.erpTaskGroupMember m JOIN webpoc.erpTaskTypeGroup p ON p.GroupCode=m.GroupCode
              WHERE m.GroupCode=st.GroupCode AND m.Username=@user AND p.TypeCode=@type AND p.CanAct=1
                AND g.Active=1 AND g.Company=@company AND g.Site=@site) THEN 1 ELSE 0 END AS Eligible
            FROM webpoc.erpApprovalRequestStage st JOIN webpoc.erpTaskGroup g ON g.Code=st.GroupCode
            WHERE st.RequestId=@request ORDER BY st.Step
            """, ct, [.. Context(s), ("@request", request["Id"]), ("@type", request["ApprovalType"])]);
        var canAct = !s.ReadOnly && approvalPermission && request["State"] == "Pending"
            && stages.Any(st => st["State"] == "Active" && st["Eligible"] == "1")
            && (request["AllowSelf"] == "True" || !string.Equals(request["CreatedBy"], s.Username, StringComparison.OrdinalIgnoreCase))
            && (request["AllowRepeat"] == "True" || !stages.Any(st => string.Equals(st["Actor"], s.Username, StringComparison.OrdinalIgnoreCase)));
        return new(Guid.Parse(request["Id"]!), reversed is null ? request["State"]! : "Reversed", request["Version"], canAct,
            stages.Select(st => new ApprovalStageResponse(int.Parse(st["Step"]!, CultureInfo.InvariantCulture),
                st["Name"]!, st["GroupName"]!, st["State"]!, st["Actor"], st["Comment"],
                st["ActedAt"] is { } date ? Stamp(date) : null)).ToArray(),
            ReversedBy: reversed?.GetValueOrDefault("Actor"), ReversalReason: reversed?.GetValueOrDefault("Reason"));
    }

    public async Task<bool> CanReverseAsync(SqlConnection c, SqlTransaction? tx, SessionResponse s, string voucherType, CancellationToken ct)
    {
        if (s.ReadOnly || !s.Capabilities.View || !s.Capabilities.Approve || !options.DevelopmentWritesApproved) return false;
        var rows = await Database.QueryAsync(c, tx, """
            SELECT 1 AS Allowed WHERE EXISTS(
              SELECT 1 FROM webpoc.erpApprovalFlow f
              JOIN webpoc.erpTaskTypeGroup p ON p.TypeCode=f.ApprovalType AND p.CanReverse=1
              JOIN webpoc.erpTaskGroup g ON g.Code=p.GroupCode AND g.Active=1 AND g.Company=f.Company AND g.Site=f.Site
              JOIN webpoc.erpTaskGroupMember m ON m.GroupCode=g.Code AND m.Username=@user
              WHERE f.Company=@company AND f.Site=@site AND f.VoucherType=@voucher AND f.Enabled=1)
            """, ct, [.. Context(s), ("@voucher", voucherType)]);
        return rows.Count == 1;
    }

    public async Task ReverseDocumentAsync(SqlConnection c, SqlTransaction tx, SessionResponse s,
        string voucherType, string id, string reference, string creator, string previousApprover, UnapproveOrderRequest request, CancellationToken ct)
    {
        Writable(s);
        if (!await CanReverseAsync(c, tx, s, voucherType, ct))
            throw new ApiFault(403, "reversal_denied", "Your group does not have approval-reversal permission.");
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 2000)
            throw new ApiFault(422, "reversal_reason", "Provide an unapproval reason (1-2000 characters).");
        var state = await ApprovalAsync(c, tx, s, id, false, ct);
        if (state.RequestId != request.RequestId || state.Version != request.RequestVersion)
            throw new ApiFault(409, "stale_approval", "The approval changed. Reload before unapproving.");
        var flow = await Database.QueryAsync(c, tx,
            "SELECT ResultType FROM webpoc.erpApprovalFlow WHERE Company=@company AND Site=@site AND VoucherType=@voucher AND Enabled=1",
            ct, [.. Context(s), ("@voucher", voucherType)]);
        var reversal = Guid.NewGuid();
        await CancelDocumentAsync(c, tx, s, id, ct);
        await Database.ExecuteAsync(c, tx, $"""
            INSERT webpoc.erpApprovalReversal(Id,Company,Prefix,Site,VoucherType,DocId,RequestId,SourceVersion,PreviousApprovedBy,Actor,Reason)
            VALUES(@reversal,@company,@prefix,@site,@voucher,@doc,@request,@version,@previous,@user,@reason);
            UPDATE webpoc.erpApprovalRequest SET State='Cancelled' WHERE Id=@request AND {Scope};
            INSERT webpoc.erpTaskHistory(TaskId,Actor,Action,Comment)
             SELECT Id,@user,'Unapproved',@reason FROM webpoc.erpTaskInstance WHERE RequestId=@request AND {Scope};
            """, ct, [.. Context(s), ("@reversal", reversal.ToString()), ("@voucher", voucherType), ("@doc", id),
                ("@request", request.RequestId?.ToString()), ("@version", request.Version), ("@previous", previousApprover), ("@reason", reason)]);
        await InsertAsync(c, tx, s, flow[0]["ResultType"]!, $"Order {reference} unapproved",
            $"{s.Username} reversed approval. Open the order for the recorded reason; edit and save to restart approval from stage 1.",
            $"reversal:{reversal}", id, null, null, null, creator, ct);
    }

    public async Task<bool> DecideAsync(SqlConnection c, SqlTransaction tx, SessionResponse s, string id,
        string sourceVersion, OrderDecisionRequest decision, bool approvalPermission, CancellationToken ct)
    {
        Writable(s);
        var comment = decision.Comment?.Trim();
        if (decision.Decision is not ("Approve" or "Reject") || comment?.Length > 2000 ||
            decision.Decision == "Reject" && string.IsNullOrEmpty(comment))
            throw new ApiFault(422, "invalid_decision", "Choose Approve or Reject. Rejection requires a reason (maximum 2000 characters).");
        var state = await ApprovalAsync(c, tx, s, id, approvalPermission, ct);
        if (state.RequestId != decision.RequestId || state.Version != decision.RequestVersion || state.State != "Pending")
            throw new ApiFault(409, "stale_approval", "This approval request changed. Reload the order before deciding.");
        if (!state.CanAct) throw new ApiFault(403, "approval_denied", "You cannot decide this approval stage.");
        var source = await Database.QueryAsync(c, tx, "SELECT SourceVersion,CreatedBy,ApprovalType,ResultType FROM webpoc.erpApprovalRequest WHERE Id=@request",
            ct, ("@request", decision.RequestId.ToString()));
        if (source[0]["SourceVersion"] != sourceVersion)
            throw new ApiFault(409, "stale_order", "The order changed outside this approval request. Edit and resubmit it before approval.");
        var current = state.Stages.Single(st => st.State == "Active");
        var next = state.Stages.FirstOrDefault(st => st.Step == current.Step + 1);
        var approved = decision.Decision == "Approve";
        var final = approved && next is null;
        var result = !approved ? "Rejected" : final ? "Approved" : "Pending";
        var taskState = approved ? "Completed" : "Rejected";
        await Database.ExecuteAsync(c, tx, """
            UPDATE webpoc.erpApprovalRequest SET State=@result WHERE Id=@request;
            UPDATE webpoc.erpApprovalRequestStage SET State=@state,Actor=@user,Comment=@comment,ActedAt=SYSDATETIMEOFFSET()
             WHERE RequestId=@request AND Step=@step;
            INSERT webpoc.erpTaskHistory(TaskId,Actor,Action,Comment)
             SELECT Id,@user,@state,@comment FROM webpoc.erpTaskInstance WHERE RequestId=@request AND Step=@step AND State='Open';
            UPDATE webpoc.erpTaskInstance SET State=@taskState WHERE RequestId=@request AND Step=@step AND State='Open';
            """, ct, ("@request", decision.RequestId.ToString()), ("@result", result), ("@state", approved ? "Approved" : "Rejected"),
            ("@user", s.Username), ("@comment", comment), ("@step", current.Step), ("@taskState", taskState));
        if (!approved)
            await Database.ExecuteAsync(c, tx, "UPDATE webpoc.erpApprovalRequestStage SET State='Cancelled' WHERE RequestId=@request AND State='Waiting'",
                ct, ("@request", decision.RequestId.ToString()));
        else if (next is not null)
        {
            await Database.ExecuteAsync(c, tx, "UPDATE webpoc.erpApprovalRequestStage SET State='Active' WHERE RequestId=@request AND Step=@step",
                ct, ("@request", decision.RequestId.ToString()), ("@step", next.Step));
            var group = await Database.QueryAsync(c, tx, "SELECT GroupCode FROM webpoc.erpApprovalRequestStage WHERE RequestId=@request AND Step=@step",
                ct, ("@request", decision.RequestId.ToString()), ("@step", next.Step));
            await InsertAsync(c, tx, s, source[0]["ApprovalType"]!, $"Approval: {next.Name}",
                $"Review approval stage {next.Step} of {state.Stages.Count}.", $"approval:{decision.RequestId}:{next.Step}",
                id, decision.RequestId, next.Step, group[0]["GroupCode"], null, ct);
        }
        if (!approved || final)
            await InsertAsync(c, tx, s, source[0]["ResultType"]!, $"Document {result.ToLowerInvariant()}",
                $"{s.Username}: {result}. Open the document to review the decision and any comments.",
                $"result:{decision.RequestId}", id, null, null, null, source[0]["CreatedBy"], ct);
        return final;
    }
}
