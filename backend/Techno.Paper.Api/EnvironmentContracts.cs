namespace Techno.Paper.Api;

public sealed record EnvironmentChoiceResponse(string Code, string Label, bool Selectable);
public sealed record EnvironmentLayoutResponse(string Key, string ControlName, string Tab, string Group,
    string? LookupId, bool Multiple, int? IntegerDigits, int? FractionDigits, int? Maximum,
    IReadOnlyList<EnvironmentChoiceResponse> Choices);
public sealed record EnvironmentFormResponse(MasterDefinitionResponse Definition, MasterRecordResponse? Record,
    IReadOnlyList<EnvironmentLayoutResponse> Layout);
