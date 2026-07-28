namespace WindowsScriptRunner.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";
    public const int MaximumHeartbeatIntervalSeconds = 3600;

    public int HeartbeatIntervalSeconds { get; set; } = 30;
}
