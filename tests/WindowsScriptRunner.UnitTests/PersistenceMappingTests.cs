using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Infrastructure.Persistence.Mapping;

namespace WindowsScriptRunner.UnitTests;

public sealed class PersistenceMappingTests
{
    [Fact]
    public void ScriptDefinitionRoundTripsCompleteAggregate()
    {
        var parameter = TestDomainFactory.Parameter(
            type: ScriptParameterType.Enum,
            allowedValues: ["Safe", "Force"]);
        var version = TestDomainFactory.Version([parameter]);
        var script = TestDomainFactory.Script(version, RiskLevel.High);
        script.Disable(TestDomainFactory.Time.AddMinutes(1));

        var restored = PersistenceMapper.ToDomain(PersistenceMapper.ToEntity(script));

        Assert.Equal(script.Id, restored.Id);
        Assert.Equal(script.Name, restored.Name);
        Assert.Equal(script.DisplayName, restored.DisplayName);
        Assert.Equal(script.Description, restored.Description);
        Assert.Equal(script.RiskLevel, restored.RiskLevel);
        Assert.Equal(script.IsEnabled, restored.IsEnabled);
        Assert.Equal(script.CreatedBy, restored.CreatedBy);
        Assert.Equal(script.CreatedUtc, restored.CreatedUtc);
        Assert.Equal(script.UpdatedUtc, restored.UpdatedUtc);
        var restoredVersion = Assert.Single(restored.Versions);
        Assert.Equal(version.Id, restoredVersion.Id);
        Assert.Equal(version.Version, restoredVersion.Version);
        Assert.True(restoredVersion.IsPublished);
        Assert.Equal(
            version.SupportedPhases.OrderBy(item => item),
            restoredVersion.SupportedPhases.OrderBy(item => item));
        Assert.Equal(
            version.SupportedReportFormats.OrderBy(item => item),
            restoredVersion.SupportedReportFormats.OrderBy(item => item));
        var restoredParameter = Assert.Single(restoredVersion.ParameterDefinitions);
        Assert.Equal(parameter.Id, restoredParameter.Id);
        Assert.Equal(parameter.AllowedValues.Order(), restoredParameter.AllowedValues.Order());
    }

    [Fact]
    public void CompleteJobRoundTripsExactLifecycleAndChildren()
    {
        var secureParameter = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            sensitive: true);
        var version = TestDomainFactory.Version([secureParameter]);
        var script = TestDomainFactory.Script(version, RiskLevel.High);
        var credentialId = CredentialReferenceId.New().ToString();
        var job = TestDomainFactory.SubmittedJob(
            script,
            version,
            [(secureParameter, credentialId)],
            ExecutionPhase.Execute);
        _ = TestDomainFactory.StartExecution(job);
        job.BeginPostValidation(TestDomainFactory.OtherUser, job.UpdatedUtc.AddMinutes(1));
        job.RecordTerminalExecutionOutcome(
            ExecutionOutcome.Succeeded,
            0,
            "Completed safely",
            TestDomainFactory.OtherUser,
            job.UpdatedUtc.AddMinutes(1));

        var restored = PersistenceMapper.ToDomain(PersistenceMapper.ToEntity(job));

        Assert.Equal(job.Id, restored.Id);
        Assert.Equal(job.ScriptDefinitionId, restored.ScriptDefinitionId);
        Assert.Equal(job.ScriptVersionId, restored.ScriptVersionId);
        Assert.Equal(JobStatus.Completed, restored.Status);
        Assert.Equal(job.RequestedPhase, restored.RequestedPhase);
        Assert.Equal(job.RequestedBy, restored.RequestedBy);
        Assert.Equal(job.LastActingUser, restored.LastActingUser);
        Assert.Equal(job.CreatedUtc, restored.CreatedUtc);
        Assert.Equal(job.UpdatedUtc, restored.UpdatedUtc);
        Assert.Equal(job.SubmittedUtc, restored.SubmittedUtc);
        Assert.Equal(job.PolicySnapshot, restored.PolicySnapshot);
        Assert.Equal(
            credentialId,
            Assert.Single(restored.Parameters).SerializedValue);
        Assert.Single(restored.Targets);
        Assert.Single(restored.Approvals);
        var execution = Assert.Single(restored.Executions);
        Assert.Equal(ExecutionOutcome.Succeeded, execution.Outcome);
        Assert.Equal(0, execution.ExitCode);
        Assert.False(execution.IsActive);
    }

    [Fact]
    public void WorkerAndCredentialReferenceRoundTrip()
    {
        var worker = new WorkerNode(
            WorkerNodeId.New(),
            "worker-01",
            TestDomainFactory.Time);
        worker.RegisterCapability(new WorkerCapability("PowerShell", "7.4"));
        worker.RecordHeartbeat(TestDomainFactory.Time.AddMinutes(1));
        worker.Disable();
        var credential = new CredentialReference(
            CredentialReferenceId.New(),
            "externalvault",
            "externalvault://vault/automation/windows",
            "Windows automation",
            TestDomainFactory.Time,
            TestDomainFactory.User,
            false);

        var restoredWorker = PersistenceMapper.ToDomain(PersistenceMapper.ToEntity(worker));
        var restoredCredential = PersistenceMapper.ToDomain(
            PersistenceMapper.ToEntity(credential));

        Assert.Equal(worker.Id, restoredWorker.Id);
        Assert.Equal(worker.Name, restoredWorker.Name);
        Assert.Equal(worker.IsEnabled, restoredWorker.IsEnabled);
        Assert.Equal(worker.LastHeartbeatUtc, restoredWorker.LastHeartbeatUtc);
        Assert.Equal(
            Assert.Single(worker.Capabilities),
            Assert.Single(restoredWorker.Capabilities));
        Assert.Equal(credential.Id, restoredCredential.Id);
        Assert.Equal(credential.ProviderType, restoredCredential.ProviderType);
        Assert.Equal(credential.ExternalIdentifier, restoredCredential.ExternalIdentifier);
        Assert.Equal(credential.DisplayName, restoredCredential.DisplayName);
        Assert.Equal(credential.IsEnabled, restoredCredential.IsEnabled);
    }

    [Fact]
    public void CorruptPartialPolicySnapshotCannotRehydrateJob()
    {
        var entity = new JobEntity
        {
            Id = Guid.NewGuid(),
            ScriptDefinitionId = Guid.NewGuid(),
            ScriptVersionId = Guid.NewGuid(),
            RequestedPhase = nameof(ExecutionPhase.Execute),
            Status = nameof(JobStatus.Submitted),
            RequestedBy = "DOMAIN\\requester",
            LastActingUser = "DOMAIN\\requester",
            CreatedUtc = TestDomainFactory.Time,
            UpdatedUtc = TestDomainFactory.Time.AddMinutes(1),
            SubmittedUtc = TestDomainFactory.Time.AddMinutes(1),
            PolicyScriptDefinitionId = Guid.NewGuid(),
        };

        Assert.Throws<DomainValidationException>(() => PersistenceMapper.ToDomain(entity));
    }

    [Fact]
    public void CorruptExecutionOutputBeforeCompletionCannotRehydrateJob()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);
        var entity = PersistenceMapper.ToEntity(TestDomainFactory.DraftJob(script, version));
        entity.Executions.Add(
            new JobExecutionEntity
            {
                Id = Guid.NewGuid(),
                JobId = entity.Id,
                AttemptNumber = 1,
                CreatedUtc = entity.CreatedUtc,
                ExitCode = 1,
            });

        Assert.Throws<DomainValidationException>(() => PersistenceMapper.ToDomain(entity));
    }

    [Fact]
    public void CorruptApprovalStateForValidationRequestCannotRehydrateJob()
    {
        var version = TestDomainFactory.Version(
            phases: [ExecutionPhase.Validation]);
        var script = TestDomainFactory.Script(version);
        var entity = PersistenceMapper.ToEntity(
            TestDomainFactory.SubmittedJob(
                script,
                version,
                requestedPhase: ExecutionPhase.Validation));
        entity.Status = nameof(JobStatus.AwaitingApproval);

        Assert.Throws<DomainValidationException>(() => PersistenceMapper.ToDomain(entity));
    }

    [Fact]
    public void SubmittedJobWithoutTargetsCannotRehydrate()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);
        var entity = PersistenceMapper.ToEntity(
            TestDomainFactory.SubmittedJob(script, version));
        entity.Targets.Clear();

        Assert.Throws<DomainValidationException>(() => PersistenceMapper.ToDomain(entity));
    }

    [Fact]
    public void ScriptVersionOutsideAggregateLifetimeCannotRehydrate()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version);
        var entity = PersistenceMapper.ToEntity(script);
        Assert.Single(entity.Versions).CreatedUtc = entity.UpdatedUtc.AddTicks(1);

        Assert.Throws<InvalidScriptVersionException>(
            () => PersistenceMapper.ToDomain(entity));
    }
}
