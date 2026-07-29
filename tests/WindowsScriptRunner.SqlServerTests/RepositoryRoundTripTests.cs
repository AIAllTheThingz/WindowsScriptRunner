using Microsoft.EntityFrameworkCore;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Workers;
using WindowsScriptRunner.Infrastructure.Persistence.Entities;
using WindowsScriptRunner.Infrastructure.Persistence.Mapping;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class RepositoryRoundTripTests
{
    [Fact]
    public async Task ScriptDefinitionRoundTripsAndUpdatesWithoutDuplicateChildren()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var enumParameter = SqlServerTestData.Parameter(
            "Mode",
            ScriptParameterType.Enum,
            allowedValues: ["Safe", "Force"]);
        var version = SqlServerTestData.Version([enumParameter]);
        var script = SqlServerTestData.Script(version);
        await using (var scope = new PersistenceTestScope(database))
        {
            await scope.Scripts.AddAsync(script, CancellationToken.None);
            await scope.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            var loaded = Assert.IsType<Domain.Scripts.ScriptDefinition>(
                await scope.Scripts.GetByIdAsync(script.Id, CancellationToken.None));
            Assert.Equal(script.Id, loaded.Id);
            Assert.Equal(script.Name, loaded.Name);
            Assert.Equal(script.DisplayName, loaded.DisplayName);
            Assert.Equal(script.Description, loaded.Description);
            Assert.Equal(script.RiskLevel, loaded.RiskLevel);
            Assert.Equal(script.IsEnabled, loaded.IsEnabled);
            Assert.Equal(script.CreatedBy, loaded.CreatedBy);
            Assert.Equal(script.CreatedUtc, loaded.CreatedUtc);
            Assert.Equal(script.UpdatedUtc, loaded.UpdatedUtc);
            var loadedVersion = Assert.Single(loaded.Versions);
            Assert.Equal(version.Id, loadedVersion.Id);
            Assert.Equal(version.Version, loadedVersion.Version);
            Assert.Equal(version.RelativeScriptPath, loadedVersion.RelativeScriptPath);
            Assert.Equal(version.Sha256, loadedVersion.Sha256);
            Assert.Equal(version.GitCommitSha, loadedVersion.GitCommitSha);
            Assert.Equal(
                version.MinimumPowerShellVersion,
                loadedVersion.MinimumPowerShellVersion);
            Assert.Equal(version.DefaultTimeoutMinutes, loadedVersion.DefaultTimeoutMinutes);
            Assert.Equal(version.CreatedUtc, loadedVersion.CreatedUtc);
            Assert.Equal(version.CreatedBy, loadedVersion.CreatedBy);
            Assert.True(loadedVersion.IsPublished);
            Assert.Equal(
                version.SupportedPhases.OrderBy(item => item),
                loadedVersion.SupportedPhases.OrderBy(item => item));
            Assert.Equal(
                version.SupportedReportFormats.OrderBy(item => item),
                loadedVersion.SupportedReportFormats.OrderBy(item => item));
            var loadedParameter = Assert.Single(loadedVersion.ParameterDefinitions);
            Assert.Equal(enumParameter.Id, loadedParameter.Id);
            Assert.Equal(enumParameter.Name, loadedParameter.Name);
            Assert.Equal(enumParameter.DisplayName, loadedParameter.DisplayName);
            Assert.Equal(enumParameter.Description, loadedParameter.Description);
            Assert.Equal(enumParameter.ParameterType, loadedParameter.ParameterType);
            Assert.Equal(enumParameter.IsRequired, loadedParameter.IsRequired);
            Assert.Equal(enumParameter.DefaultValue, loadedParameter.DefaultValue);
            Assert.Equal(enumParameter.IsSensitive, loadedParameter.IsSensitive);
            Assert.Equal(
                enumParameter.AllowedValues.Order(),
                loadedParameter.AllowedValues.Order());

            loaded.UpdateDetails(
                "Updated test script",
                "Updated through SQL repository",
                loaded.UpdatedUtc.AddMinutes(1));
            loaded.AddVersion(
                SqlServerTestData.Version(version: "2.0.0"),
                loaded.UpdatedUtc.AddMinutes(1));
            await scope.Scripts.UpdateAsync(loaded, CancellationToken.None);
            await scope.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            var reloaded = Assert.IsType<Domain.Scripts.ScriptDefinition>(
                await scope.Scripts.GetByIdAsync(script.Id, CancellationToken.None));
            Assert.Equal("Updated test script", reloaded.DisplayName);
            Assert.Equal(2, reloaded.Versions.Count);
            Assert.Equal(
                2,
                await scope.Context.ScriptVersions.CountAsync(
                    item => item.ScriptDefinitionId == script.Id.Value));
            Assert.Equal(
                1,
                await scope.Context.ScriptParameterDefinitions.CountAsync(
                    item => item.ScriptVersionId == version.Id.Value));
            Assert.Equal(
                2,
                await scope.Context.ScriptParameterAllowedValues.CountAsync());
        }
    }

    [Fact]
    public async Task CompleteJobRoundTripsAndSensitiveResponseRemainsRedacted()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var secureParameter = SqlServerTestData.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            sensitive: true);
        var version = SqlServerTestData.Version([secureParameter]);
        var script = SqlServerTestData.Script(version);
        var credential = SqlServerTestData.Credential();
        var job = SqlServerTestData.CompleteExecuteJob(
            script,
            version,
            [(secureParameter, credential.Id.ToString())]);
        await using (var scope = new PersistenceTestScope(database))
        {
            await scope.Scripts.AddAsync(script, CancellationToken.None);
            await scope.Credentials.AddAsync(credential, CancellationToken.None);
            await scope.Jobs.AddAsync(job, CancellationToken.None);
            await scope.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            var loaded = Assert.IsType<Job>(
                await scope.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
            Assert.Equal(job.Id, loaded.Id);
            Assert.Equal(job.ScriptDefinitionId, loaded.ScriptDefinitionId);
            Assert.Equal(job.ScriptVersionId, loaded.ScriptVersionId);
            Assert.Equal(JobStatus.Completed, loaded.Status);
            Assert.Equal(job.RequestedPhase, loaded.RequestedPhase);
            Assert.Equal(job.RequestedBy, loaded.RequestedBy);
            Assert.Equal(job.LastActingUser, loaded.LastActingUser);
            Assert.Equal(job.CreatedUtc, loaded.CreatedUtc);
            Assert.Equal(job.UpdatedUtc, loaded.UpdatedUtc);
            Assert.Equal(job.SubmittedUtc, loaded.SubmittedUtc);
            Assert.Equal(job.Description, loaded.Description);
            Assert.Equal(job.ChangeReference, loaded.ChangeReference);
            Assert.Equal(job.PolicySnapshot, loaded.PolicySnapshot);
            var target = Assert.Single(loaded.Targets);
            var expectedTarget = Assert.Single(job.Targets);
            Assert.Equal(expectedTarget.Name, target.Name);
            Assert.Equal(expectedTarget.AddedBy, target.AddedBy);
            Assert.Equal(expectedTarget.AddedUtc, target.AddedUtc);
            var parameter = Assert.Single(loaded.Parameters);
            Assert.Equal(secureParameter.Name, parameter.Name);
            Assert.Equal(
                credential.Id.ToString(),
                parameter.SerializedValue);
            var approval = Assert.Single(loaded.Approvals);
            var expectedApproval = Assert.Single(job.Approvals);
            Assert.Equal(expectedApproval.Id, approval.Id);
            Assert.Equal(expectedApproval.Decision, approval.Decision);
            Assert.Equal(expectedApproval.Approver, approval.Approver);
            Assert.Equal(expectedApproval.DecisionUtc, approval.DecisionUtc);
            Assert.Equal(expectedApproval.Comment, approval.Comment);
            Assert.Equal(expectedApproval.ApprovalFingerprint, approval.ApprovalFingerprint);
            var execution = Assert.Single(loaded.Executions);
            var expectedExecution = Assert.Single(job.Executions);
            Assert.Equal(expectedExecution.Id, execution.Id);
            Assert.Equal(expectedExecution.AttemptNumber, execution.AttemptNumber);
            Assert.Equal(expectedExecution.WorkerNodeId, execution.WorkerNodeId);
            Assert.Equal(expectedExecution.CreatedUtc, execution.CreatedUtc);
            Assert.Equal(expectedExecution.StartedUtc, execution.StartedUtc);
            Assert.Equal(expectedExecution.CompletedUtc, execution.CompletedUtc);
            Assert.Equal(expectedExecution.Outcome, execution.Outcome);
            Assert.Equal(expectedExecution.ExitCode, execution.ExitCode);
            Assert.Equal(expectedExecution.Summary, execution.Summary);
            var response = await new GetJobHandler(scope.Jobs, scope.Scripts)
                .HandleAsync(new GetJobQuery(job.Id), CancellationToken.None);
            var responseParameter = Assert.Single(response.Parameters);
            Assert.Equal("[REDACTED]", responseParameter.DisplayValue);
            Assert.True(responseParameter.IsSensitive);
            Assert.True(responseParameter.IsRedacted);
            Assert.DoesNotContain(
                credential.Id.ToString(),
                responseParameter.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(
                credential.Id.ToString(),
                await scope.Context.JobParameters
                    .Where(item => item.JobId == job.Id.Value)
                    .Select(item => item.SerializedValue)
                    .SingleAsync());
        }
    }

    [Fact]
    public async Task JobLifecycleVariantsAndOptionalClearRoundTrip()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var optional = SqlServerTestData.Parameter("Optional");
        var version = SqlServerTestData.Version([optional]);
        var script = SqlServerTestData.Script(version);
        var draft = SqlServerTestData.DraftJob(script, version);
        draft.SetParameterValue("Optional", "present", SqlServerTestData.Requester, SqlServerTestData.Time);
        var submitted = SqlServerTestData.SubmittedJob(script, version);
        var approved = SqlServerTestData.SubmittedJob(script, version);
        approved.MarkValidated(SqlServerTestData.Approver, approved.UpdatedUtc.AddMinutes(1));
        approved.QueueDryRun(SqlServerTestData.Approver, approved.UpdatedUtc.AddMinutes(1));
        approved.StartDryRun(SqlServerTestData.Approver, approved.UpdatedUtc.AddMinutes(1));
        approved.CompleteDryRun(SqlServerTestData.Approver, approved.UpdatedUtc.AddMinutes(1));
        approved.RequireApproval(SqlServerTestData.Approver, approved.UpdatedUtc.AddMinutes(1));
        approved.RecordApproval(
            SqlServerTestData.Approver,
            SqlServerTestData.Fingerprint,
            null,
            approved.UpdatedUtc.AddMinutes(1));
        var worker = new WorkerNode(
            WorkerNodeId.New(),
            "worker-lifecycle",
            SqlServerTestData.Time);
        var executing = SqlServerTestData.SubmittedJob(script, version);
        executing.MarkValidated(SqlServerTestData.Approver, executing.UpdatedUtc.AddMinutes(1));
        executing.QueueDryRun(SqlServerTestData.Approver, executing.UpdatedUtc.AddMinutes(1));
        executing.StartDryRun(SqlServerTestData.Approver, executing.UpdatedUtc.AddMinutes(1));
        executing.CompleteDryRun(SqlServerTestData.Approver, executing.UpdatedUtc.AddMinutes(1));
        executing.RequireApproval(SqlServerTestData.Approver, executing.UpdatedUtc.AddMinutes(1));
        executing.RecordApproval(
            SqlServerTestData.Approver,
            SqlServerTestData.Fingerprint,
            null,
            executing.UpdatedUtc.AddMinutes(1));
        executing.QueueExecution(SqlServerTestData.Approver, executing.UpdatedUtc.AddMinutes(1));
        var lease = executing.AcquireWorkLease(
            JobLeaseId.New(),
            worker.Id,
            JobWorkKind.Execute,
            1,
            SqlServerTestData.Approver,
            executing.UpdatedUtc.AddMinutes(1),
            executing.UpdatedUtc.AddMinutes(3));
        executing.StartLeasedExecutionAttempt(
            lease.Credentials,
            SqlServerTestData.Approver,
            executing.UpdatedUtc.AddMinutes(1));
        await using (var scope = new PersistenceTestScope(database))
        {
            await scope.Scripts.AddAsync(script, CancellationToken.None);
            await scope.Workers.AddAsync(worker, CancellationToken.None);
            await scope.Jobs.AddAsync(draft, CancellationToken.None);
            await scope.Jobs.AddAsync(submitted, CancellationToken.None);
            await scope.Jobs.AddAsync(approved, CancellationToken.None);
            await scope.Jobs.AddAsync(executing, CancellationToken.None);
            await scope.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            var loadedDraft = Assert.IsType<Job>(
                await scope.Jobs.GetByIdAsync(draft.Id, CancellationToken.None));
            _ = loadedDraft.ClearParameterValue(
                "Optional",
                SqlServerTestData.Requester,
                loadedDraft.UpdatedUtc.AddMinutes(1));
            await scope.Jobs.UpdateAsync(loadedDraft, CancellationToken.None);
            await scope.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            Assert.Equal(
                JobStatus.Submitted,
                Assert.IsType<Job>(
                    await scope.Jobs.GetByIdAsync(submitted.Id, CancellationToken.None)).Status);
            Assert.Equal(
                JobStatus.Approved,
                Assert.IsType<Job>(
                    await scope.Jobs.GetByIdAsync(approved.Id, CancellationToken.None)).Status);
            Assert.Equal(
                JobStatus.Executing,
                Assert.IsType<Job>(
                    await scope.Jobs.GetByIdAsync(executing.Id, CancellationToken.None)).Status);
            Assert.False(await scope.Context.JobParameters.AnyAsync(
                item => item.JobId == draft.Id.Value));
        }
    }

    [Fact]
    public async Task ValidationDryRunAndReadOnlyCompletionPathsRoundTrip()
    {
        await using var database = await SqlServerDatabase.CreateAsync();

        var validationVersion = SqlServerTestData.Version(
            phases: [ExecutionPhase.Validation]);
        var validationScript = SqlServerTestData.Script(
            validationVersion,
            name: "validation.script");
        var validationJob = SqlServerTestData.SubmittedJob(
            validationScript,
            validationVersion,
            phase: ExecutionPhase.Validation);
        validationJob.MarkValidated(
            SqlServerTestData.Approver,
            validationJob.UpdatedUtc.AddMinutes(1));
        validationJob.CompleteRequestedValidation(
            SqlServerTestData.Approver,
            validationJob.UpdatedUtc.AddMinutes(1));

        var dryRunVersion = SqlServerTestData.Version(
            phases: [ExecutionPhase.Validation, ExecutionPhase.DryRun]);
        var dryRunScript = SqlServerTestData.Script(
            dryRunVersion,
            name: "dryrun.script");
        var dryRunJob = SqlServerTestData.SubmittedJob(
            dryRunScript,
            dryRunVersion,
            phase: ExecutionPhase.DryRun);
        AdvanceToCompletedDryRun(dryRunJob);
        dryRunJob.CompleteRequestedDryRun(
            SqlServerTestData.Approver,
            dryRunJob.UpdatedUtc.AddMinutes(1));

        var readOnlyVersion = SqlServerTestData.Version(
            phases: [ExecutionPhase.Validation, ExecutionPhase.DryRun]);
        var readOnlyScript = SqlServerTestData.Script(
            readOnlyVersion,
            RiskLevel.ReadOnly,
            "readonly.script");
        var readOnlyJob = SqlServerTestData.SubmittedJob(
            readOnlyScript,
            readOnlyVersion,
            phase: ExecutionPhase.DryRun);
        AdvanceToCompletedDryRun(readOnlyJob);
        readOnlyJob.CompleteReadOnlyAfterDryRun(
            SqlServerTestData.Approver,
            readOnlyJob.UpdatedUtc.AddMinutes(1));

        await using (var scope = new PersistenceTestScope(database))
        {
            await scope.Scripts.AddAsync(validationScript, CancellationToken.None);
            await scope.Scripts.AddAsync(dryRunScript, CancellationToken.None);
            await scope.Scripts.AddAsync(readOnlyScript, CancellationToken.None);
            await scope.Jobs.AddAsync(validationJob, CancellationToken.None);
            await scope.Jobs.AddAsync(dryRunJob, CancellationToken.None);
            await scope.Jobs.AddAsync(readOnlyJob, CancellationToken.None);
            await scope.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var verification = new PersistenceTestScope(database);
        await AssertCompletedWithoutExecutionAsync(
            verification,
            validationJob.Id,
            ExecutionPhase.Validation);
        await AssertCompletedWithoutExecutionAsync(
            verification,
            dryRunJob.Id,
            ExecutionPhase.DryRun);
        await AssertCompletedWithoutExecutionAsync(
            verification,
            readOnlyJob.Id,
            ExecutionPhase.DryRun);
    }

    [Fact]
    public async Task JobLifecycleChildGraphUpdatesWithoutDuplicateRows()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var version = SqlServerTestData.Version();
        var script = SqlServerTestData.Script(version);
        var job = SqlServerTestData.DraftJob(script, version);
        var worker = new WorkerNode(
            WorkerNodeId.New(),
            "worker-child-graph",
            SqlServerTestData.Time);
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Scripts.AddAsync(script, CancellationToken.None);
            await seed.Jobs.AddAsync(job, CancellationToken.None);
            await seed.Workers.AddAsync(worker, CancellationToken.None);
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var submission = new PersistenceTestScope(database))
        {
            var loadedJob = Assert.IsType<Job>(
                await submission.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
            var loadedScript = Assert.IsType<Domain.Scripts.ScriptDefinition>(
                await submission.Scripts.GetByIdAsync(script.Id, CancellationToken.None));
            loadedJob.AddTarget(
                new Domain.ValueObjects.TargetName("server-01"),
                SqlServerTestData.Requester,
                loadedJob.UpdatedUtc.AddMinutes(1));
            loadedJob.Submit(
                loadedScript,
                SqlServerTestData.Requester,
                loadedJob.UpdatedUtc.AddMinutes(1));
            await submission.Jobs.UpdateAsync(loadedJob, CancellationToken.None);
            await submission.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var executionStart = new PersistenceTestScope(database))
        {
            var loaded = Assert.IsType<Job>(
                await executionStart.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
            loaded.MarkValidated(SqlServerTestData.Approver, loaded.UpdatedUtc.AddMinutes(1));
            loaded.QueueDryRun(SqlServerTestData.Approver, loaded.UpdatedUtc.AddMinutes(1));
            loaded.StartDryRun(SqlServerTestData.Approver, loaded.UpdatedUtc.AddMinutes(1));
            loaded.CompleteDryRun(SqlServerTestData.Approver, loaded.UpdatedUtc.AddMinutes(1));
            loaded.RequireApproval(SqlServerTestData.Approver, loaded.UpdatedUtc.AddMinutes(1));
            loaded.RecordApproval(
                SqlServerTestData.Approver,
                SqlServerTestData.Fingerprint,
                "Approved",
                loaded.UpdatedUtc.AddMinutes(1));
            loaded.QueueExecution(SqlServerTestData.Approver, loaded.UpdatedUtc.AddMinutes(1));
            var lease = loaded.AcquireWorkLease(
                JobLeaseId.New(),
                worker.Id,
                JobWorkKind.Execute,
                1,
                SqlServerTestData.Approver,
                loaded.UpdatedUtc.AddMinutes(1),
                loaded.UpdatedUtc.AddMinutes(10));
            loaded.StartLeasedExecutionAttempt(
                lease.Credentials,
                SqlServerTestData.Approver,
                loaded.UpdatedUtc.AddMinutes(1));
            await executionStart.Jobs.UpdateAsync(loaded, CancellationToken.None);
            await executionStart.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var completion = new PersistenceTestScope(database))
        {
            var loaded = Assert.IsType<Job>(
                await completion.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
            loaded.BeginPostValidation(
                loaded.Lease!.Credentials,
                SqlServerTestData.Approver,
                loaded.UpdatedUtc.AddMinutes(1));
            loaded.RecordTerminalExecutionOutcome(
                loaded.Lease.Credentials,
                ExecutionOutcome.Succeeded,
                0,
                "Completed",
                SqlServerTestData.Approver,
                loaded.UpdatedUtc.AddMinutes(1));
            await completion.Jobs.UpdateAsync(loaded, CancellationToken.None);
            await completion.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using var verification = new PersistenceTestScope(database);
        var restored = Assert.IsType<Job>(
            await verification.Jobs.GetByIdAsync(job.Id, CancellationToken.None));
        Assert.Equal(JobStatus.Completed, restored.Status);
        Assert.Single(restored.Targets);
        Assert.Single(restored.Approvals);
        Assert.Single(restored.Executions);
        Assert.Equal(
            1,
            await verification.Context.JobApprovals.CountAsync(
                item => item.JobId == job.Id.Value));
        Assert.Equal(
            1,
            await verification.Context.JobExecutions.CountAsync(
                item => item.JobId == job.Id.Value));
    }

    [Fact]
    public async Task WorkerCredentialAndAuditRoundTripWithoutSecretColumns()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var worker = SqlServerTestData.Worker();
        var credential = SqlServerTestData.Credential();
        var audit = new AuditEvent(
            AuditEventId.New(),
            "WorkerRegistered",
            "WorkerNode",
            worker.Id.ToString(),
            SqlServerTestData.Requester,
            SqlServerTestData.Time,
            "Worker registered",
            new Dictionary<string, string>
            {
                ["CapabilityCount"] = "1",
            });
        await using (var scope = new PersistenceTestScope(database))
        {
            await scope.Workers.AddAsync(worker, CancellationToken.None);
            await scope.Credentials.AddAsync(credential, CancellationToken.None);
            await scope.Audits.WriteAsync(audit, CancellationToken.None);
            await scope.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var scope = new PersistenceTestScope(database))
        {
            var loadedWorker = Assert.IsType<Domain.Workers.WorkerNode>(
                await scope.Workers.GetByIdAsync(worker.Id, CancellationToken.None));
            var loadedCredential = Assert.IsType<Domain.Credentials.CredentialReference>(
                await scope.Credentials.GetByIdAsync(credential.Id, CancellationToken.None));
            Assert.Equal(worker.Id, loadedWorker.Id);
            Assert.Equal(worker.Name, loadedWorker.Name);
            Assert.Equal(worker.IsEnabled, loadedWorker.IsEnabled);
            Assert.Equal(worker.RegisteredUtc, loadedWorker.RegisteredUtc);
            Assert.Equal(worker.LastHeartbeatUtc, loadedWorker.LastHeartbeatUtc);
            Assert.Equal(
                Assert.Single(worker.Capabilities),
                Assert.Single(loadedWorker.Capabilities));
            Assert.Equal(credential.Id, loadedCredential.Id);
            Assert.Equal(credential.ProviderType, loadedCredential.ProviderType);
            Assert.Equal(credential.ExternalIdentifier, loadedCredential.ExternalIdentifier);
            Assert.Equal(credential.DisplayName, loadedCredential.DisplayName);
            Assert.Equal(credential.IsEnabled, loadedCredential.IsEnabled);
            Assert.Equal(credential.CreatedUtc, loadedCredential.CreatedUtc);
            Assert.Equal(credential.CreatedBy, loadedCredential.CreatedBy);
            var auditEntity = await scope.Context.AuditEvents
                .Include(item => item.Properties)
                .SingleAsync(item => item.Id == audit.Id.Value);
            Assert.Equal(audit.EventType, auditEntity.EventType);
            Assert.Equal("CapabilityCount", Assert.Single(auditEntity.Properties).Key);
            var credentialColumns = await scope.Context.Database.SqlQueryRaw<string>(
                """
                SELECT [COLUMN_NAME] AS [Value]
                FROM [INFORMATION_SCHEMA].[COLUMNS]
                WHERE [TABLE_SCHEMA] = N'wsr'
                  AND [TABLE_NAME] = N'CredentialReferences'
                """).ToListAsync();
            Assert.DoesNotContain(
                credentialColumns,
                name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CredentialValue", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task CredentialDuplicateAndHashCollisionAreRejectedBeforeStaging()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var credential = SqlServerTestData.Credential();
        await using (var stagedScope = new PersistenceTestScope(database))
        {
            await stagedScope.Credentials.AddAsync(credential, CancellationToken.None);
            var stagedDuplicate = new CredentialReference(
                CredentialReferenceId.New(),
                credential.ProviderType.ToUpperInvariant(),
                credential.ExternalIdentifier,
                "Staged duplicate",
                SqlServerTestData.Time,
                SqlServerTestData.Requester);

            var exception = await Assert.ThrowsAsync<ApplicationConflictException>(
                () => stagedScope.Credentials.AddAsync(
                    stagedDuplicate,
                    CancellationToken.None));

            Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
            Assert.Single(
                stagedScope.Context.ChangeTracker
                    .Entries<CredentialReferenceEntity>(),
                entry => entry.State == EntityState.Added);
        }

        var collisionCandidate = new CredentialReference(
            CredentialReferenceId.New(),
            credential.ProviderType,
            "externalvault://vault/automation/other",
            "Other credential",
            SqlServerTestData.Time,
            SqlServerTestData.Requester);
        await using (var seed = new PersistenceTestScope(database))
        {
            await seed.Credentials.AddAsync(credential, CancellationToken.None);
            seed.Context.CredentialReferences.Add(
                new CredentialReferenceEntity
                {
                    Id = Guid.NewGuid(),
                    ProviderType = collisionCandidate.ProviderType,
                    NormalizedProviderType = PersistenceMapper.Normalize(
                        collisionCandidate.ProviderType),
                    ExternalIdentifier =
                        "externalvault://vault/automation/collision-source",
                    ExternalIdentifierHash = PersistenceMapper.HashExternalIdentifier(
                        collisionCandidate.ExternalIdentifier),
                    DisplayName = "Collision source",
                    IsEnabled = true,
                    CreatedUtc = SqlServerTestData.Time,
                    CreatedBy = SqlServerTestData.Requester.Value,
                });
            await seed.UnitOfWork.CommitAsync(CancellationToken.None);
        }

        await using (var duplicateScope = new PersistenceTestScope(database))
        {
            var duplicate = new CredentialReference(
                CredentialReferenceId.New(),
                credential.ProviderType.ToUpperInvariant(),
                credential.ExternalIdentifier,
                "Duplicate",
                SqlServerTestData.Time,
                SqlServerTestData.Requester);
            var exception = await Assert.ThrowsAsync<ApplicationConflictException>(
                () => duplicateScope.Credentials.AddAsync(
                    duplicate,
                    CancellationToken.None));
            Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(
                duplicateScope.Context.ChangeTracker.Entries<CredentialReferenceEntity>(),
                entry => entry.State == EntityState.Added);
        }

        await using (var collisionScope = new PersistenceTestScope(database))
        {
            var exception = await Assert.ThrowsAsync<ApplicationConflictException>(
                () => collisionScope.Credentials.AddAsync(
                    collisionCandidate,
                    CancellationToken.None));
            Assert.Contains("hash collision", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(
                collisionScope.Context.ChangeTracker.Entries<CredentialReferenceEntity>(),
                entry => entry.State == EntityState.Added);
        }
    }

    private static void AdvanceToCompletedDryRun(Job job)
    {
        job.MarkValidated(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        job.QueueDryRun(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        job.StartDryRun(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
        job.CompleteDryRun(SqlServerTestData.Approver, job.UpdatedUtc.AddMinutes(1));
    }

    private static async Task AssertCompletedWithoutExecutionAsync(
        PersistenceTestScope scope,
        JobId jobId,
        ExecutionPhase requestedPhase)
    {
        var loaded = Assert.IsType<Job>(
            await scope.Jobs.GetByIdAsync(jobId, CancellationToken.None));
        Assert.Equal(JobStatus.Completed, loaded.Status);
        Assert.Equal(requestedPhase, loaded.RequestedPhase);
        Assert.Empty(loaded.Executions);
        Assert.Empty(loaded.Approvals);
    }
}
