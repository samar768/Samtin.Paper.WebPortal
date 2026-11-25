namespace Techno.Paper.Api;

/// <summary>One authorized task; transaction links are generated from registered entity adapters.</summary>
public sealed record ErpTaskResponse(Guid Id, string Type, string Category, string Title, string Body,
    string State, DateTimeOffset CreatedAt, string CreatedBy, bool Read, string? Href);
/// <summary>Bounded inbox page and count of unread active tasks in the selected workspace.</summary>
public sealed record TaskInboxResponse(IReadOnlyList<ErpTaskResponse> Items, int Unread, bool HasMore);
/// <summary>A task type the current user may publish manually.</summary>
public sealed record PublishableTaskTypeResponse(string Code, string Name);
/// <summary>Memo/note publication with an idempotency key. Audience comes from configured type groups.</summary>
public sealed record PublishTaskRequest(string Type, string Title, string Body, Guid IdempotencyKey);
/// <summary>One snapshotted sequential approval stage, with its immutable decision when completed.</summary>
public sealed record ApprovalStageResponse(int Step, string Name, string Group, string State,
    string? Actor, string? Comment, DateTimeOffset? ActedAt);
/// <summary>Current approval request and whether this actor can decide its active stage.</summary>
public sealed record ApprovalStateResponse(Guid? RequestId, string State, string? Version, bool CanAct,
    IReadOnlyList<ApprovalStageResponse> Stages, bool CanReverse = false, string? ReversedBy = null, string? ReversalReason = null);
/// <summary>Concurrency-protected decision on the currently displayed order and approval request.</summary>
public sealed record OrderDecisionRequest(string Version, Guid RequestId, string RequestVersion, string Decision, string? Comment);
/// <summary>Reasoned reversal protected by both document and latest workflow versions.</summary>
public sealed record UnapproveOrderRequest(string Version, Guid? RequestId, string? RequestVersion, string Reason);
