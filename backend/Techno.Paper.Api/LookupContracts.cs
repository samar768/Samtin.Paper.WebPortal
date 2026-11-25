namespace Techno.Paper.Api;

/// <summary>One explicitly configured table column; no SQL expressions reach the browser.</summary>
public sealed record LookupColumnResponse(string Key, string Label, int Width);
/// <summary>Versioned SQL-driven value-help presentation metadata.</summary>
public sealed record LookupDefinitionResponse(string Id, string Title, string Version, int PageSize,
    IReadOnlyList<LookupColumnResponse> Columns, LookupCreateResponse? Create = null);
/// <summary>Immutable reference key with display-only description and configured table cells.</summary>
public sealed record LookupOptionResponse(string Key, string Label, Dictionary<string, string?> Cells);
/// <summary>One bounded search page, with current metadata and a continuation indicator.</summary>
public sealed record LookupSearchResponse(LookupDefinitionResponse Definition, IReadOnlyList<LookupOptionResponse> Options,
    int Page, bool HasMore);
/// <summary>Resolve at most 100 existing reference keys without relying on the first search page.</summary>
public sealed record ResolveLookupRequest(IReadOnlyList<string> Keys);
/// <summary>Exact-key descriptions and explicit unresolved keys in the current authorized scope.</summary>
public sealed record LookupResolveResponse(LookupDefinitionResponse Definition, IReadOnlyList<LookupOptionResponse> Options,
    IReadOnlyList<string> MissingKeys);
