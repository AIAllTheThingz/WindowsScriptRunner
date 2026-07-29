namespace WindowsScriptRunner.Contracts.Common;

public sealed record ErrorResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    string? CorrelationId = null);
