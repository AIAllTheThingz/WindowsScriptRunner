namespace WindowsScriptRunner.Infrastructure.Persistence;

public sealed class SqlServerPersistenceOptions
{
    public const string SectionName = "Persistence";

    public int CommandTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public bool ApplyMigrationsOnStartup { get; set; }
    public bool EnableDetailedErrors { get; set; }

    internal bool IsValid() =>
        CommandTimeoutSeconds is >= 1 and <= 300 &&
        RetryCount is >= 0 and <= 10 &&
        RetryDelaySeconds is >= 0 and <= 60;
}
