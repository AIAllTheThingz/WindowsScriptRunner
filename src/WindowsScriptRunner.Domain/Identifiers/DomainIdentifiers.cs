using WindowsScriptRunner.Domain.Exceptions;

namespace WindowsScriptRunner.Domain.Identifiers;

public sealed record ScriptDefinitionId
{
    public ScriptDefinitionId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(ScriptDefinitionId));
    public Guid Value { get; }
    public static ScriptDefinitionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record ScriptVersionId
{
    public ScriptVersionId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(ScriptVersionId));
    public Guid Value { get; }
    public static ScriptVersionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record ScriptParameterDefinitionId
{
    public ScriptParameterDefinitionId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(ScriptParameterDefinitionId));
    public Guid Value { get; }
    public static ScriptParameterDefinitionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record JobId
{
    public JobId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(JobId));
    public Guid Value { get; }
    public static JobId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record JobExecutionId
{
    public JobExecutionId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(JobExecutionId));
    public Guid Value { get; }
    public static JobExecutionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record JobLeaseId
{
    public JobLeaseId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(JobLeaseId));
    public Guid Value { get; }
    public static JobLeaseId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record JobApprovalId
{
    public JobApprovalId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(JobApprovalId));
    public Guid Value { get; }
    public static JobApprovalId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record JobLogEntryId
{
    public JobLogEntryId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(JobLogEntryId));
    public Guid Value { get; }
    public static JobLogEntryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record JobReportId
{
    public JobReportId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(JobReportId));
    public Guid Value { get; }
    public static JobReportId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record WorkerNodeId
{
    public WorkerNodeId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(WorkerNodeId));
    public Guid Value { get; }
    public static WorkerNodeId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record AuditEventId
{
    public AuditEventId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(AuditEventId));
    public Guid Value { get; }
    public static AuditEventId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record CredentialReferenceId
{
    public CredentialReferenceId(Guid value) => Value = IdentifierGuard.NotEmpty(value, nameof(CredentialReferenceId));
    public Guid Value { get; }
    public static CredentialReferenceId New() => new(Guid.NewGuid());
    public static bool TryParse(string? value, out CredentialReferenceId? id)
    {
        id = null;
        var candidate = value?.Trim();
        if (!Guid.TryParseExact(candidate, "D", out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        id = new CredentialReferenceId(parsed);
        return true;
    }

    public override string ToString() => Value.ToString("D");
}

internal static class IdentifierGuard
{
    public static Guid NotEmpty(Guid value, string typeName)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException($"{typeName} cannot contain an empty GUID.");
        }

        return value;
    }
}
