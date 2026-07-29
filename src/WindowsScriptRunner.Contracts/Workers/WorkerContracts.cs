namespace WindowsScriptRunner.Contracts.Workers;

public sealed record WorkerSummaryResponse(
    Guid Id,
    string Name,
    bool IsEnabled,
    DateTimeOffset RegisteredUtc,
    DateTimeOffset? LastHeartbeatUtc,
    IReadOnlyDictionary<string, string> Capabilities);
