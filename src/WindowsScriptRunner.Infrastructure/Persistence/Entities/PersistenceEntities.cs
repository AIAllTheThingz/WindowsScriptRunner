namespace WindowsScriptRunner.Infrastructure.Persistence.Entities;

internal sealed class ScriptDefinitionEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<ScriptVersionEntity> Versions { get; } = [];
}

internal sealed class ScriptVersionEntity
{
    public Guid Id { get; set; }
    public Guid ScriptDefinitionId { get; set; }
    public int Major { get; set; }
    public int Minor { get; set; }
    public int Patch { get; set; }
    public string RelativeScriptPath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string? GitCommitSha { get; set; }
    public string MinimumPowerShellVersion { get; set; } = string.Empty;
    public int DefaultTimeoutMinutes { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public ScriptDefinitionEntity ScriptDefinition { get; set; } = null!;
    public List<ScriptVersionPhaseEntity> SupportedPhases { get; } = [];
    public List<ScriptVersionReportFormatEntity> SupportedReportFormats { get; } = [];
    public List<ScriptParameterDefinitionEntity> ParameterDefinitions { get; } = [];
}

internal sealed class ScriptVersionPhaseEntity
{
    public Guid ScriptVersionId { get; set; }
    public string Phase { get; set; } = string.Empty;
    public ScriptVersionEntity ScriptVersion { get; set; } = null!;
}

internal sealed class ScriptVersionReportFormatEntity
{
    public Guid ScriptVersionId { get; set; }
    public string ReportFormat { get; set; } = string.Empty;
    public ScriptVersionEntity ScriptVersion { get; set; } = null!;
}

internal sealed class ScriptParameterDefinitionEntity
{
    public Guid Id { get; set; }
    public Guid ScriptVersionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ParameterType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsSensitive { get; set; }
    public ScriptVersionEntity ScriptVersion { get; set; } = null!;
    public List<ScriptParameterAllowedValueEntity> AllowedValues { get; } = [];
}

internal sealed class ScriptParameterAllowedValueEntity
{
    public Guid ScriptParameterDefinitionId { get; set; }
    public string Value { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public ScriptParameterDefinitionEntity ScriptParameterDefinition { get; set; } = null!;
}

internal sealed class JobEntity
{
    public Guid Id { get; set; }
    public Guid ScriptDefinitionId { get; set; }
    public Guid ScriptVersionId { get; set; }
    public string RequestedPhase { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string LastActingUser { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? SubmittedUtc { get; set; }
    public string? Description { get; set; }
    public string? ChangeReference { get; set; }
    public Guid? PolicyScriptDefinitionId { get; set; }
    public Guid? PolicyScriptVersionId { get; set; }
    public string? PolicyRiskLevel { get; set; }
    public bool? PolicySupportsExecute { get; set; }
    public bool? PolicySupportsPostValidation { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<JobTargetEntity> Targets { get; } = [];
    public List<JobParameterEntity> Parameters { get; } = [];
    public List<JobExecutionEntity> Executions { get; } = [];
    public List<JobApprovalEntity> Approvals { get; } = [];
}

internal sealed class JobTargetEntity
{
    public Guid JobId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTimeOffset AddedUtc { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public JobEntity Job { get; set; } = null!;
}

internal sealed class JobParameterEntity
{
    public Guid JobId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string SerializedValue { get; set; } = string.Empty;
    public JobEntity Job { get; set; } = null!;
}

internal sealed class JobExecutionEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public int AttemptNumber { get; set; }
    public Guid? WorkerNodeId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string? Outcome { get; set; }
    public int? ExitCode { get; set; }
    public string? Summary { get; set; }
    public JobEntity Job { get; set; } = null!;
}

internal sealed class JobApprovalEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string Approver { get; set; } = string.Empty;
    public DateTimeOffset DecisionUtc { get; set; }
    public string? Comment { get; set; }
    public string ApprovalFingerprint { get; set; } = string.Empty;
    public JobEntity Job { get; set; } = null!;
}

internal sealed class WorkerNodeEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTimeOffset RegisteredUtc { get; set; }
    public DateTimeOffset? LastHeartbeatUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<WorkerCapabilityEntity> Capabilities { get; } = [];
}

internal sealed class WorkerCapabilityEntity
{
    public Guid WorkerNodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public WorkerNodeEntity WorkerNode { get; set; } = null!;
}

internal sealed class CredentialReferenceEntity
{
    public Guid Id { get; set; }
    public string ProviderType { get; set; } = string.Empty;
    public string NormalizedProviderType { get; set; } = string.Empty;
    public string ExternalIdentifier { get; set; } = string.Empty;
    public byte[] ExternalIdentifierHash { get; set; } = [];
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class AuditEventEntity
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<AuditEventPropertyEntity> Properties { get; } = [];
}

internal sealed class AuditEventPropertyEntity
{
    public Guid AuditEventId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NormalizedKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public AuditEventEntity AuditEvent { get; set; } = null!;
}
