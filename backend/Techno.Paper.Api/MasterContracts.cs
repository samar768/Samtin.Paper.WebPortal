namespace Techno.Paper.Api;

public sealed record MasterCapabilitiesResponse(bool View, bool Add, bool Edit, bool Delete, bool Print, bool Audit);
public sealed record MasterDefinitionResponse(string Id, string Title, int Revision, string Version,
    IReadOnlyList<FieldResponse> Fields, Dictionary<string, string?> Defaults, MasterCapabilitiesResponse Capabilities,
    IReadOnlyList<string> LookupIds, string Scope, bool CaptureAuditValues);
public sealed record MasterRecordResponse(string Id, string Version, string MetadataVersion,
    Dictionary<string, string?> Values, RecordInfoResponse RecordInfo);
public sealed record MasterFormResponse(MasterDefinitionResponse Definition, MasterRecordResponse? Record);
public sealed record MasterListResponse(MasterDefinitionResponse Definition, IReadOnlyList<MasterRecordResponse> Records,
    int Page, bool HasMore);
public sealed record SaveMasterRequest(string MetadataVersion, string? Version, string IdempotencyKey,
    Dictionary<string, string?> Values);
public sealed record MasterPrintResponse(MasterDefinitionResponse Definition, IReadOnlyList<MasterRecordResponse> Records);
public sealed record LookupCreateResponse(string MasterId, string Title);
public sealed record MasterAuditEntryResponse(string Id, string RecordId, string Username, string Operation,
    string OccurredAt, string? CompanyCode, string? SiteCode, int? MetadataRevision, bool ValuesCaptured,
    Dictionary<string, string?>? Before, Dictionary<string, string?>? After);
public sealed record MasterAuditResponse(MasterDefinitionResponse Definition, IReadOnlyList<MasterAuditEntryResponse> Entries,
    int Page, bool HasMore);
