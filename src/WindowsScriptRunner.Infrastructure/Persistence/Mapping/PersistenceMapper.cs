using System.Security.Cryptography;
using System.Text;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;

namespace WindowsScriptRunner.Infrastructure.Persistence.Mapping;

internal static class PersistenceMapper
{
    public static ScriptDefinitionEntity ToEntity(ScriptDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var entity = new ScriptDefinitionEntity
        {
            Id = definition.Id.Value,
            Name = definition.Name.Value,
            NormalizedName = Normalize(definition.Name.Value),
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            RiskLevel = definition.RiskLevel.ToString(),
            IsEnabled = definition.IsEnabled,
            CreatedBy = definition.CreatedBy.Value,
            CreatedUtc = ToUtc(definition.CreatedUtc),
            UpdatedUtc = ToUtc(definition.UpdatedUtc),
        };
        foreach (var version in definition.Versions
            .OrderBy(item => item.Version.Major)
            .ThenBy(item => item.Version.Minor)
            .ThenBy(item => item.Version.Patch))
        {
            entity.Versions.Add(ToEntity(version, entity.Id));
        }

        return entity;
    }

    public static ScriptDefinition ToDomain(ScriptDefinitionEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var versions = entity.Versions
            .OrderBy(version => version.Major)
            .ThenBy(version => version.Minor)
            .ThenBy(version => version.Patch)
            .Select(ToDomain)
            .ToArray();
        return ScriptDefinition.Rehydrate(
            new ScriptDefinitionId(entity.Id),
            new ScriptName(entity.Name),
            entity.DisplayName,
            entity.Description,
            ParseEnum<RiskLevel>(entity.RiskLevel, "script risk level"),
            entity.IsEnabled,
            new UserIdentity(entity.CreatedBy),
            entity.CreatedUtc,
            entity.UpdatedUtc,
            versions);
    }

    public static void Synchronize(ScriptDefinition definition, ScriptDefinitionEntity entity)
    {
        RequireSame(
            definition.Id.Value == entity.Id &&
            definition.Name.Value == entity.Name &&
            definition.RiskLevel.ToString() == entity.RiskLevel &&
            definition.CreatedBy.Value == entity.CreatedBy &&
            ToUtc(definition.CreatedUtc) == entity.CreatedUtc,
            "Script definition immutable persistence state does not match the aggregate.");
        entity.DisplayName = definition.DisplayName;
        entity.Description = definition.Description;
        entity.IsEnabled = definition.IsEnabled;
        entity.UpdatedUtc = ToUtc(definition.UpdatedUtc);

        foreach (var version in definition.Versions)
        {
            var persisted = entity.Versions.SingleOrDefault(item => item.Id == version.Id.Value);
            if (persisted is null)
            {
                entity.Versions.Add(ToEntity(version, entity.Id));
            }
            else
            {
                Synchronize(version, persisted);
            }
        }

        RequireSame(
            entity.Versions.All(item =>
                definition.Versions.Any(version => version.Id.Value == item.Id)),
            "Persisted script versions cannot be removed through an aggregate update.");
    }

    public static JobEntity ToEntity(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var entity = new JobEntity
        {
            Id = job.Id.Value,
            ScriptDefinitionId = job.ScriptDefinitionId.Value,
            ScriptVersionId = job.ScriptVersionId.Value,
            RequestedPhase = job.RequestedPhase.ToString(),
            Status = job.Status.ToString(),
            RequestedBy = job.RequestedBy.Value,
            LastActingUser = job.LastActingUser.Value,
            CreatedUtc = ToUtc(job.CreatedUtc),
            UpdatedUtc = ToUtc(job.UpdatedUtc),
            SubmittedUtc = ToUtc(job.SubmittedUtc),
            Description = job.Description,
            ChangeReference = job.ChangeReference?.Value,
        };
        SetPolicySnapshot(entity, job.PolicySnapshot);
        foreach (var target in job.Targets.OrderBy(item => Normalize(item.Name.Value)))
        {
            entity.Targets.Add(ToEntity(target, entity.Id));
        }

        foreach (var parameter in job.Parameters.OrderBy(item => Normalize(item.Name)))
        {
            entity.Parameters.Add(ToEntity(parameter, entity.Id));
        }

        foreach (var execution in job.Executions.OrderBy(item => item.AttemptNumber))
        {
            entity.Executions.Add(ToEntity(execution, entity.Id));
        }

        foreach (var approval in job.Approvals.OrderBy(item => item.DecisionUtc))
        {
            entity.Approvals.Add(ToEntity(approval, entity.Id));
        }

        if (job.Lease is not null)
        {
            entity.Lease = ToEntity(job.Lease, entity.Id);
        }

        return entity;
    }

    public static Job ToDomain(JobEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var snapshot = ToDomainPolicySnapshot(entity);
        return Job.Rehydrate(
            new JobId(entity.Id),
            new ScriptDefinitionId(entity.ScriptDefinitionId),
            new ScriptVersionId(entity.ScriptVersionId),
            ParseEnum<ExecutionPhase>(entity.RequestedPhase, "job requested phase"),
            ParseEnum<JobStatus>(entity.Status, "job status"),
            new UserIdentity(entity.RequestedBy),
            new UserIdentity(entity.LastActingUser),
            entity.CreatedUtc,
            entity.UpdatedUtc,
            entity.SubmittedUtc,
            entity.Description,
            entity.ChangeReference is null ? null : new ChangeReference(entity.ChangeReference),
            snapshot,
            entity.Targets
                .OrderBy(item => item.NormalizedName)
                .Select(item => new JobTarget(
                    new TargetName(item.Name),
                    item.AddedUtc,
                    new UserIdentity(item.AddedBy)))
                .ToArray(),
            entity.Parameters
                .OrderBy(item => item.NormalizedName)
                .Select(item => new JobParameter(item.Name, item.SerializedValue))
                .ToArray(),
            entity.Executions
                .OrderBy(item => item.AttemptNumber)
                .Select(ToDomain)
                .ToArray(),
            entity.Approvals
                .OrderBy(item => item.DecisionUtc)
                .Select(item => new JobApproval(
                    new JobApprovalId(item.Id),
                    ParseEnum<ApprovalDecision>(item.Decision, "approval decision"),
                    new UserIdentity(item.Approver),
                    item.DecisionUtc,
                    item.Comment,
                    item.ApprovalFingerprint))
                .ToArray(),
            entity.Lease is null ? null : ToDomain(entity.Lease));
    }

    public static void Synchronize(Job job, JobEntity entity)
    {
        RequireSame(
            job.Id.Value == entity.Id &&
            job.ScriptDefinitionId.Value == entity.ScriptDefinitionId &&
            job.ScriptVersionId.Value == entity.ScriptVersionId &&
            job.RequestedPhase.ToString() == entity.RequestedPhase &&
            job.RequestedBy.Value == entity.RequestedBy &&
            ToUtc(job.CreatedUtc) == entity.CreatedUtc,
            "Job immutable persistence state does not match the aggregate.");

        entity.Status = job.Status.ToString();
        entity.LastActingUser = job.LastActingUser.Value;
        entity.UpdatedUtc = ToUtc(job.UpdatedUtc);
        entity.SubmittedUtc = ToUtc(job.SubmittedUtc);
        entity.Description = job.Description;
        entity.ChangeReference = job.ChangeReference?.Value;
        SetPolicySnapshot(entity, job.PolicySnapshot);
        SynchronizeTargets(job, entity);
        SynchronizeParameters(job, entity);
        SynchronizeExecutions(job, entity);
        SynchronizeApprovals(job, entity);
        SynchronizeLease(job, entity);
    }

    public static WorkerNodeEntity ToEntity(WorkerNode worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        var entity = new WorkerNodeEntity
        {
            Id = worker.Id.Value,
            Name = worker.Name,
            NormalizedName = Normalize(worker.Name),
            IsEnabled = worker.IsEnabled,
            RegisteredUtc = ToUtc(worker.RegisteredUtc),
            LastHeartbeatUtc = ToUtc(worker.LastHeartbeatUtc),
        };
        foreach (var capability in worker.Capabilities.OrderBy(item => Normalize(item.Name)))
        {
            entity.Capabilities.Add(ToEntity(capability, entity.Id));
        }

        return entity;
    }

    public static WorkerNode ToDomain(WorkerNodeEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return WorkerNode.Rehydrate(
            new WorkerNodeId(entity.Id),
            entity.Name,
            entity.IsEnabled,
            entity.RegisteredUtc,
            entity.LastHeartbeatUtc,
            entity.Capabilities
                .OrderBy(item => item.NormalizedName)
                .Select(item => new WorkerCapability(item.Name, item.Value))
                .ToArray());
    }

    public static void Synchronize(WorkerNode worker, WorkerNodeEntity entity)
    {
        RequireSame(
            worker.Id.Value == entity.Id &&
            worker.Name == entity.Name &&
            ToUtc(worker.RegisteredUtc) == entity.RegisteredUtc,
            "Worker immutable persistence state does not match the aggregate.");
        entity.IsEnabled = worker.IsEnabled;
        entity.LastHeartbeatUtc = ToUtc(worker.LastHeartbeatUtc);

        var desired = worker.Capabilities.ToDictionary(
            capability => Normalize(capability.Name),
            StringComparer.Ordinal);
        entity.Capabilities.RemoveAll(capability => !desired.ContainsKey(capability.NormalizedName));
        foreach (var capability in desired)
        {
            var persisted = entity.Capabilities.SingleOrDefault(
                item => item.NormalizedName == capability.Key);
            if (persisted is null)
            {
                entity.Capabilities.Add(ToEntity(capability.Value, entity.Id));
            }
            else
            {
                persisted.Name = capability.Value.Name;
                persisted.Value = capability.Value.Value;
            }
        }
    }

    public static CredentialReferenceEntity ToEntity(CredentialReference credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return new CredentialReferenceEntity
        {
            Id = credential.Id.Value,
            ProviderType = credential.ProviderType,
            NormalizedProviderType = Normalize(credential.ProviderType),
            ExternalIdentifier = credential.ExternalIdentifier,
            ExternalIdentifierHash = HashExternalIdentifier(credential.ExternalIdentifier),
            DisplayName = credential.DisplayName,
            IsEnabled = credential.IsEnabled,
            CreatedUtc = ToUtc(credential.CreatedUtc),
            CreatedBy = credential.CreatedBy.Value,
        };
    }

    public static CredentialReference ToDomain(CredentialReferenceEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var expectedHash = HashExternalIdentifier(entity.ExternalIdentifier);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, entity.ExternalIdentifierHash))
        {
            throw new DomainValidationException(
                "Persisted credential reference identifier hash is invalid.");
        }

        return CredentialReference.Rehydrate(
            new CredentialReferenceId(entity.Id),
            entity.ProviderType,
            entity.ExternalIdentifier,
            entity.DisplayName,
            entity.CreatedUtc,
            new UserIdentity(entity.CreatedBy),
            entity.IsEnabled);
    }

    public static void Synchronize(
        CredentialReference credential,
        CredentialReferenceEntity entity)
    {
        RequireSame(
            credential.Id.Value == entity.Id &&
            credential.ProviderType == entity.ProviderType &&
            credential.ExternalIdentifier == entity.ExternalIdentifier &&
            credential.DisplayName == entity.DisplayName &&
            ToUtc(credential.CreatedUtc) == entity.CreatedUtc &&
            credential.CreatedBy.Value == entity.CreatedBy,
            "Credential reference immutable persistence state does not match the aggregate.");
        entity.IsEnabled = credential.IsEnabled;
    }

    public static AuditEventEntity ToEntity(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var entity = new AuditEventEntity
        {
            Id = auditEvent.Id.Value,
            EventType = auditEvent.EventType,
            EntityType = auditEvent.EntityType,
            EntityId = auditEvent.EntityId,
            Actor = auditEvent.Actor.Value,
            OccurredUtc = ToUtc(auditEvent.OccurredUtc),
            Summary = auditEvent.Summary,
        };
        foreach (var property in auditEvent.Properties.OrderBy(item => Normalize(item.Key)))
        {
            entity.Properties.Add(new AuditEventPropertyEntity
            {
                AuditEventId = entity.Id,
                Key = property.Key,
                NormalizedKey = Normalize(property.Key),
                Value = property.Value,
            });
        }

        return entity;
    }

    public static byte[] HashExternalIdentifier(string externalIdentifier) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(externalIdentifier));

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static ScriptVersionEntity ToEntity(
        ScriptVersion version,
        Guid scriptDefinitionId)
    {
        var entity = new ScriptVersionEntity
        {
            Id = version.Id.Value,
            ScriptDefinitionId = scriptDefinitionId,
            Major = version.Version.Major,
            Minor = version.Version.Minor,
            Patch = version.Version.Patch,
            RelativeScriptPath = version.RelativeScriptPath,
            Sha256 = version.Sha256,
            GitCommitSha = version.GitCommitSha,
            MinimumPowerShellVersion = version.MinimumPowerShellVersion,
            DefaultTimeoutMinutes = version.DefaultTimeoutMinutes,
            IsPublished = version.IsPublished,
            CreatedUtc = ToUtc(version.CreatedUtc),
            CreatedBy = version.CreatedBy.Value,
        };
        foreach (var phase in version.SupportedPhases.OrderBy(item => item.ToString()))
        {
            entity.SupportedPhases.Add(new ScriptVersionPhaseEntity
            {
                ScriptVersionId = entity.Id,
                Phase = phase.ToString(),
            });
        }

        foreach (var format in version.SupportedReportFormats.OrderBy(item => item.ToString()))
        {
            entity.SupportedReportFormats.Add(new ScriptVersionReportFormatEntity
            {
                ScriptVersionId = entity.Id,
                ReportFormat = format.ToString(),
            });
        }

        foreach (var definition in version.ParameterDefinitions.OrderBy(item => Normalize(item.Name)))
        {
            entity.ParameterDefinitions.Add(ToEntity(definition, entity.Id));
        }

        return entity;
    }

    private static ScriptVersion ToDomain(ScriptVersionEntity entity) =>
        ScriptVersion.Rehydrate(
            new ScriptVersionId(entity.Id),
            new ScriptVersionNumber(entity.Major, entity.Minor, entity.Patch),
            entity.RelativeScriptPath,
            entity.Sha256,
            entity.GitCommitSha,
            entity.MinimumPowerShellVersion,
            entity.DefaultTimeoutMinutes,
            entity.SupportedPhases
                .OrderBy(item => item.Phase)
                .Select(item => ParseEnum<ExecutionPhase>(item.Phase, "script version phase"))
                .ToArray(),
            entity.SupportedReportFormats
                .OrderBy(item => item.ReportFormat)
                .Select(item => ParseEnum<ReportFormat>(item.ReportFormat, "script report format"))
                .ToArray(),
            entity.CreatedUtc,
            new UserIdentity(entity.CreatedBy),
            entity.IsPublished,
            entity.ParameterDefinitions
                .OrderBy(item => item.NormalizedName)
                .Select(ToDomain)
                .ToArray());

    private static ScriptParameterDefinition ToDomain(ScriptParameterDefinitionEntity entity) =>
        new(
            new ScriptParameterDefinitionId(entity.Id),
            entity.Name,
            entity.DisplayName,
            entity.Description,
            ParseEnum<ScriptParameterType>(entity.ParameterType, "script parameter type"),
            entity.IsRequired,
            entity.DefaultValue,
            entity.AllowedValues
                .OrderBy(item => item.NormalizedValue)
                .Select(item => item.Value)
                .ToArray(),
            entity.IsSensitive);

    private static ScriptParameterDefinitionEntity ToEntity(
        ScriptParameterDefinition definition,
        Guid scriptVersionId)
    {
        var entity = new ScriptParameterDefinitionEntity
        {
            Id = definition.Id.Value,
            ScriptVersionId = scriptVersionId,
            Name = definition.Name,
            NormalizedName = Normalize(definition.Name),
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            ParameterType = definition.ParameterType.ToString(),
            IsRequired = definition.IsRequired,
            DefaultValue = definition.DefaultValue,
            IsSensitive = definition.IsSensitive,
        };
        foreach (var allowedValue in definition.AllowedValues.OrderBy(Normalize))
        {
            entity.AllowedValues.Add(new ScriptParameterAllowedValueEntity
            {
                ScriptParameterDefinitionId = entity.Id,
                Value = allowedValue,
                NormalizedValue = Normalize(allowedValue),
            });
        }

        return entity;
    }

    private static void Synchronize(ScriptVersion version, ScriptVersionEntity entity)
    {
        RequireSame(
            version.Id.Value == entity.Id &&
            version.Version.Major == entity.Major &&
            version.Version.Minor == entity.Minor &&
            version.Version.Patch == entity.Patch &&
            version.RelativeScriptPath == entity.RelativeScriptPath &&
            version.Sha256 == entity.Sha256 &&
            version.GitCommitSha == entity.GitCommitSha &&
            version.MinimumPowerShellVersion == entity.MinimumPowerShellVersion &&
            version.DefaultTimeoutMinutes == entity.DefaultTimeoutMinutes &&
            ToUtc(version.CreatedUtc) == entity.CreatedUtc &&
            version.CreatedBy.Value == entity.CreatedBy,
            "Script version immutable persistence state does not match the aggregate.");
        RequireSame(
            version.SupportedPhases.Select(item => item.ToString()).ToHashSet(StringComparer.Ordinal)
                .SetEquals(entity.SupportedPhases.Select(item => item.Phase)),
            "Script version phases are immutable.");
        RequireSame(
            version.SupportedReportFormats.Select(item => item.ToString()).ToHashSet(StringComparer.Ordinal)
                .SetEquals(entity.SupportedReportFormats.Select(item => item.ReportFormat)),
            "Script version report formats are immutable.");
        entity.IsPublished = version.IsPublished;

        foreach (var definition in version.ParameterDefinitions)
        {
            var persisted = entity.ParameterDefinitions.SingleOrDefault(
                item => item.Id == definition.Id.Value);
            if (persisted is null)
            {
                entity.ParameterDefinitions.Add(ToEntity(definition, entity.Id));
            }
            else
            {
                RequireSame(
                    persisted.Name == definition.Name &&
                    persisted.DisplayName == definition.DisplayName &&
                    persisted.Description == definition.Description &&
                    persisted.ParameterType == definition.ParameterType.ToString() &&
                    persisted.IsRequired == definition.IsRequired &&
                    persisted.DefaultValue == definition.DefaultValue &&
                    persisted.IsSensitive == definition.IsSensitive &&
                    definition.AllowedValues.Select(Normalize).ToHashSet(StringComparer.Ordinal)
                        .SetEquals(persisted.AllowedValues.Select(item => item.NormalizedValue)),
                    "Script parameter definitions are immutable.");
            }
        }

        RequireSame(
            entity.ParameterDefinitions.All(item =>
                version.ParameterDefinitions.Any(definition => definition.Id.Value == item.Id)),
            "Script parameter definitions cannot be removed through persistence.");
    }

    private static JobTargetEntity ToEntity(JobTarget target, Guid jobId) =>
        new()
        {
            JobId = jobId,
            Name = target.Name.Value,
            NormalizedName = Normalize(target.Name.Value),
            AddedUtc = ToUtc(target.AddedUtc),
            AddedBy = target.AddedBy.Value,
        };

    private static JobParameterEntity ToEntity(JobParameter parameter, Guid jobId) =>
        new()
        {
            JobId = jobId,
            Name = parameter.Name,
            NormalizedName = Normalize(parameter.Name),
            SerializedValue = parameter.SerializedValue,
        };

    private static JobExecutionEntity ToEntity(JobExecution execution, Guid jobId) =>
        new()
        {
            Id = execution.Id.Value,
            JobId = jobId,
            AttemptNumber = execution.AttemptNumber,
            WorkerNodeId = execution.WorkerNodeId?.Value,
            CreatedUtc = ToUtc(execution.CreatedUtc),
            StartedUtc = ToUtc(execution.StartedUtc),
            CompletedUtc = ToUtc(execution.CompletedUtc),
            Outcome = execution.Outcome?.ToString(),
            ExitCode = execution.ExitCode,
            Summary = execution.Summary,
        };

    private static JobExecution ToDomain(JobExecutionEntity entity) =>
        JobExecution.Rehydrate(
            new JobExecutionId(entity.Id),
            entity.AttemptNumber,
            entity.WorkerNodeId is null ? null : new WorkerNodeId(entity.WorkerNodeId.Value),
            entity.CreatedUtc,
            entity.StartedUtc,
            entity.CompletedUtc,
            entity.Outcome is null
                ? null
                : ParseEnum<ExecutionOutcome>(entity.Outcome, "execution outcome"),
            entity.ExitCode,
            entity.Summary);

    private static JobLeaseEntity ToEntity(JobLease lease, Guid jobId) =>
        new()
        {
            JobId = jobId,
            LeaseId = lease.Id.Value,
            WorkerNodeId = lease.WorkerNodeId.Value,
            WorkKind = lease.WorkKind.ToString(),
            FencingToken = lease.FencingToken,
            AcquiredUtc = ToUtc(lease.AcquiredUtc),
            LastRenewedUtc = ToUtc(lease.LastRenewedUtc),
            ExpiresUtc = ToUtc(lease.ExpiresUtc),
        };

    private static JobLease ToDomain(JobLeaseEntity entity) =>
        new(
            new JobLeaseId(entity.LeaseId),
            new WorkerNodeId(entity.WorkerNodeId),
            ParseEnum<JobWorkKind>(entity.WorkKind, "job lease work kind"),
            entity.FencingToken,
            entity.AcquiredUtc,
            entity.LastRenewedUtc,
            entity.ExpiresUtc);

    private static JobApprovalEntity ToEntity(JobApproval approval, Guid jobId) =>
        new()
        {
            Id = approval.Id.Value,
            JobId = jobId,
            Decision = approval.Decision.ToString(),
            Approver = approval.Approver.Value,
            DecisionUtc = ToUtc(approval.DecisionUtc),
            Comment = approval.Comment,
            ApprovalFingerprint = approval.ApprovalFingerprint,
        };

    private static void SynchronizeTargets(Job job, JobEntity entity)
    {
        var desired = job.Targets.ToDictionary(
            target => Normalize(target.Name.Value),
            StringComparer.Ordinal);
        entity.Targets.RemoveAll(target => !desired.ContainsKey(target.NormalizedName));
        foreach (var target in desired)
        {
            var persisted = entity.Targets.SingleOrDefault(
                item => item.NormalizedName == target.Key);
            if (persisted is null)
            {
                entity.Targets.Add(ToEntity(target.Value, entity.Id));
            }
            else
            {
                RequireSame(
                    persisted.Name == target.Value.Name.Value &&
                    persisted.AddedUtc == ToUtc(target.Value.AddedUtc) &&
                    persisted.AddedBy == target.Value.AddedBy.Value,
                    "Job target immutable persistence state does not match the aggregate.");
            }
        }
    }

    public static void SynchronizeLease(Job job, JobEntity entity)
    {
        if (job.Lease is null)
        {
            entity.Lease = null;
            return;
        }

        if (entity.Lease is null)
        {
            entity.Lease = ToEntity(job.Lease, entity.Id);
            return;
        }

        RequireSame(
            entity.Lease.JobId == entity.Id &&
            entity.Lease.LeaseId == job.Lease.Id.Value &&
            entity.Lease.WorkerNodeId == job.Lease.WorkerNodeId.Value &&
            entity.Lease.WorkKind == job.Lease.WorkKind.ToString() &&
            entity.Lease.FencingToken == job.Lease.FencingToken &&
            entity.Lease.AcquiredUtc == ToUtc(job.Lease.AcquiredUtc),
            "Job lease immutable persistence state does not match the aggregate.");
        entity.Lease.LastRenewedUtc = ToUtc(job.Lease.LastRenewedUtc);
        entity.Lease.ExpiresUtc = ToUtc(job.Lease.ExpiresUtc);
    }

    private static void SynchronizeParameters(Job job, JobEntity entity)
    {
        var desired = job.Parameters.ToDictionary(
            parameter => Normalize(parameter.Name),
            StringComparer.Ordinal);
        entity.Parameters.RemoveAll(parameter => !desired.ContainsKey(parameter.NormalizedName));
        foreach (var parameter in desired)
        {
            var persisted = entity.Parameters.SingleOrDefault(
                item => item.NormalizedName == parameter.Key);
            if (persisted is null)
            {
                entity.Parameters.Add(ToEntity(parameter.Value, entity.Id));
            }
            else
            {
                persisted.Name = parameter.Value.Name;
                persisted.SerializedValue = parameter.Value.SerializedValue;
            }
        }
    }

    private static void SynchronizeExecutions(Job job, JobEntity entity)
    {
        foreach (var execution in job.Executions)
        {
            var persisted = entity.Executions.SingleOrDefault(item => item.Id == execution.Id.Value);
            if (persisted is null)
            {
                entity.Executions.Add(ToEntity(execution, entity.Id));
                continue;
            }

            RequireSame(
                persisted.JobId == entity.Id &&
                persisted.AttemptNumber == execution.AttemptNumber &&
                persisted.WorkerNodeId == execution.WorkerNodeId?.Value &&
                persisted.CreatedUtc == ToUtc(execution.CreatedUtc),
                "Job execution immutable persistence state does not match the aggregate.");
            persisted.StartedUtc = ToUtc(execution.StartedUtc);
            persisted.CompletedUtc = ToUtc(execution.CompletedUtc);
            persisted.Outcome = execution.Outcome?.ToString();
            persisted.ExitCode = execution.ExitCode;
            persisted.Summary = execution.Summary;
        }

        RequireSame(
            entity.Executions.All(item =>
                job.Executions.Any(execution => execution.Id.Value == item.Id)),
            "Job executions cannot be removed through persistence.");
    }

    private static void SynchronizeApprovals(Job job, JobEntity entity)
    {
        foreach (var approval in job.Approvals)
        {
            var persisted = entity.Approvals.SingleOrDefault(item => item.Id == approval.Id.Value);
            if (persisted is null)
            {
                entity.Approvals.Add(ToEntity(approval, entity.Id));
            }
            else
            {
                RequireSame(
                    persisted.Decision == approval.Decision.ToString() &&
                    persisted.Approver == approval.Approver.Value &&
                    persisted.DecisionUtc == ToUtc(approval.DecisionUtc) &&
                    persisted.Comment == approval.Comment &&
                    persisted.ApprovalFingerprint == approval.ApprovalFingerprint,
                    "Job approval immutable persistence state does not match the aggregate.");
            }
        }

        RequireSame(
            entity.Approvals.All(item =>
                job.Approvals.Any(approval => approval.Id.Value == item.Id)),
            "Job approvals cannot be removed through persistence.");
    }

    private static WorkerCapabilityEntity ToEntity(
        WorkerCapability capability,
        Guid workerNodeId) =>
        new()
        {
            WorkerNodeId = workerNodeId,
            Name = capability.Name,
            NormalizedName = Normalize(capability.Name),
            Value = capability.Value,
        };

    private static void SetPolicySnapshot(JobEntity entity, JobPolicySnapshot? snapshot)
    {
        entity.PolicyScriptDefinitionId = snapshot?.ScriptDefinitionId.Value;
        entity.PolicyScriptVersionId = snapshot?.ScriptVersionId.Value;
        entity.PolicyRiskLevel = snapshot?.RiskLevel.ToString();
        entity.PolicySupportsExecute = snapshot?.SupportsExecutePhase;
        entity.PolicySupportsPostValidation = snapshot?.SupportsPostValidationPhase;
    }

    private static JobPolicySnapshot? ToDomainPolicySnapshot(JobEntity entity)
    {
        var valuesPresent = new[]
        {
            entity.PolicyScriptDefinitionId is not null,
            entity.PolicyScriptVersionId is not null,
            entity.PolicyRiskLevel is not null,
            entity.PolicySupportsExecute is not null,
            entity.PolicySupportsPostValidation is not null,
        };
        if (valuesPresent.All(value => !value))
        {
            return null;
        }

        if (valuesPresent.Any(value => !value))
        {
            throw new DomainValidationException(
                "Persisted job policy snapshot is incomplete.");
        }

        return JobPolicySnapshot.Rehydrate(
            new ScriptDefinitionId(entity.PolicyScriptDefinitionId!.Value),
            new ScriptVersionId(entity.PolicyScriptVersionId!.Value),
            ParseEnum<RiskLevel>(entity.PolicyRiskLevel!, "job policy risk level"),
            entity.PolicySupportsExecute!.Value,
            entity.PolicySupportsPostValidation!.Value);
    }

    private static TEnum ParseEnum<TEnum>(string value, string fieldName)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, false, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new DomainValidationException(
                $"Persisted {fieldName} contains an unsupported value.");
        }

        return parsed;
    }

    private static DateTimeOffset ToUtc(DateTimeOffset value) => value.ToUniversalTime();
    private static DateTimeOffset? ToUtc(DateTimeOffset? value) => value?.ToUniversalTime();

    private static void RequireSame(bool condition, string message)
    {
        if (!condition)
        {
            throw new ApplicationValidationException(message);
        }
    }
}
