namespace WindowsScriptRunner.Contracts.Scripts;

public sealed record ScriptDefinitionSummaryResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string RiskLevel,
    bool IsEnabled,
    DateTimeOffset UpdatedUtc);

public sealed record ScriptVersionSummaryResponse(
    Guid Id,
    string Version,
    bool IsPublished,
    IReadOnlyList<string> SupportedPhases,
    IReadOnlyList<string> SupportedReportFormats);
