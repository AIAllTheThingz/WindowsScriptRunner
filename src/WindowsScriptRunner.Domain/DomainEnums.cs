namespace WindowsScriptRunner.Domain;

public enum JobStatus
{
    Draft,
    Submitted,
    Validated,
    DryRunQueued,
    DryRunRunning,
    DryRunCompleted,
    AwaitingApproval,
    Approved,
    ExecutionQueued,
    Claimed,
    Executing,
    PostValidation,
    Completed,
    CompletedWithWarnings,
    Failed,
    Rejected,
    Cancelled,
    TimedOut,
    Blocked,
    NotRun,
}

public enum RiskLevel
{
    ReadOnly,
    Low,
    Medium,
    High,
    Critical,
}

public enum ExecutionPhase
{
    Discovery,
    Validation,
    DryRun,
    Report,
    Execute,
    PostValidation,
}

public enum LogStream
{
    Output,
    Error,
    Warning,
    Verbose,
    Debug,
    Information,
    Progress,
    System,
}

public enum ScriptParameterType
{
    String,
    StringArray,
    Integer,
    Boolean,
    DateTime,
    Enum,
    SecureReference,
}

public enum ReportFormat
{
    Text,
    Csv,
    Json,
    Html,
}

public enum ApprovalDecision
{
    Pending,
    Approved,
    Rejected,
}

public enum ExecutionOutcome
{
    Succeeded,
    SucceededWithWarnings,
    Failed,
    Cancelled,
    TimedOut,
    Blocked,
    NotRun,
}
