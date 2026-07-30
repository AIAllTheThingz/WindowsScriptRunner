namespace WindowsScriptRunner.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";
    public const int MaximumHeartbeatIntervalSeconds = 3600;
    public const int MaximumConcurrentJobs = 32;
    public const int MaximumCandidateBatchSize = 100;

    public Guid NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int WorkerStaleAfterSeconds { get; set; } = 90;
    public int QueuePollingIntervalMilliseconds { get; set; } = 1000;
    public int EmptyQueueBackoffMaximumSeconds { get; set; } = 30;
    public int PersistenceFailureBackoffMaximumSeconds { get; set; } = 30;
    public int LeaseDurationSeconds { get; set; } = 120;
    public int LeaseRenewalIntervalSeconds { get; set; } = 30;
    public int LeaseRecoveryIntervalSeconds { get; set; } = 30;
    public int DrainTimeoutSeconds { get; set; } = 60;
    public int MaxConcurrentJobs { get; set; } = 1;
    public int ClaimCandidateBatchSize { get; set; } = 10;
    public bool QueueProcessingEnabled { get; set; } = true;
    public bool AllowEphemeralNodeId { get; set; }
    public List<WorkerCapabilityOptions> Capabilities { get; set; } = [];
}

public sealed class WorkerCapabilityOptions
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
