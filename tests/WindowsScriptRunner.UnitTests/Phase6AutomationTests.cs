using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Automation;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.UnitTests;

public sealed class Phase6AutomationTests
{
    private static readonly DateTimeOffset Time =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReviewedCatalogAcceptsOnlyExactPublishedPackage()
    {
        var definition = LocalHostInventoryPackageMetadata.CreateDefinition(Time);
        var version = Assert.Single(definition.Versions);

        LocalHostInventoryArtifactCatalog.ValidatePackage(definition, version);

        var mismatchedDefinition = DefinitionWithVersion(
            Version(sha256: new string('a', 64)));
        Assert.Throws<AutomationPackageTrustException>(
            () => LocalHostInventoryArtifactCatalog.ValidatePackage(
                mismatchedDefinition,
                Assert.Single(mismatchedDefinition.Versions)));
    }

    [Fact]
    public void ReviewedPackageHasNoCommandLineParameters()
    {
        var definition = LocalHostInventoryPackageMetadata.CreateDefinition(Time);
        var version = Assert.Single(definition.Versions);
        var job = SubmittedJob(definition, version);

        var arguments = LocalHostInventoryParameterMapper.Map(
            job,
            version,
            LocalHostInventoryPackageMetadata.AllowedParameterNames);

        Assert.Empty(arguments);
    }

    [Theory]
    [InlineData(ScriptParameterType.String, true)]
    [InlineData(ScriptParameterType.SecureReference, true)]
    public void SensitiveAndSecureReferenceParametersAreRejected(
        ScriptParameterType type,
        bool sensitive)
    {
        var parameter = new ScriptParameterDefinition(
            ScriptParameterDefinitionId.New(),
            "Input",
            "Input",
            null,
            type,
            true,
            null,
            null,
            sensitive);
        var version = Version(parameter: parameter);
        var definition = DefinitionWithVersion(version, "test.parameter");
        var job = Job.CreateDraft(
            JobId.New(),
            definition.Id,
            version.Id,
            ExecutionPhase.DryRun,
            new UserIdentity("DOMAIN\\requester"),
            Time);
        job.AddTarget(
            new TargetName("local-worker"),
            new UserIdentity("DOMAIN\\requester"),
            Time);
        job.SetParameterValue(
            parameter.Name,
            type == ScriptParameterType.SecureReference
                ? CredentialReferenceId.New().ToString()
                : "private-value",
            new UserIdentity("DOMAIN\\requester"),
            Time);
        job.Submit(definition, new UserIdentity("DOMAIN\\requester"), Time);

        Assert.Throws<AutomationPackageTrustException>(
            () => LocalHostInventoryParameterMapper.Map(
                job,
                version,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    parameter.Name,
                }));
    }

    [Theory]
    [InlineData(PowerShellTerminationReason.Exited, 0, true, null)]
    [InlineData(PowerShellTerminationReason.Exited, 12, false, ExecutionOutcome.Failed)]
    [InlineData(PowerShellTerminationReason.TimedOut, null, false, ExecutionOutcome.TimedOut)]
    [InlineData(
        PowerShellTerminationReason.OutputLimitExceeded,
        null,
        false,
        ExecutionOutcome.Failed)]
    public void StructuredResultsMapToBoundedLifecycleOutcomes(
        PowerShellTerminationReason reason,
        int? exitCode,
        bool succeeded,
        ExecutionOutcome? outcome)
    {
        var result = new PowerShellExecutionResult(
            PowerShellExecutionId.New(),
            new PowerShellRuntimeInfo(
                "omitted",
                new Version(7, 4),
                "Core",
                "Win32NT",
                "Windows",
                "X64",
                false),
            Time,
            Time.AddSeconds(1),
            TimeSpan.FromSeconds(1),
            exitCode,
            "inventory-output-must-not-be-mapped",
            "error-output-must-not-be-mapped",
            32,
            0,
            false,
            false,
            reason);

        var mapping = LocalHostInventoryResultMapper.Map(result);

        Assert.Equal(succeeded, mapping.Succeeded);
        Assert.Equal(outcome, mapping.Outcome);
        Assert.DoesNotContain(
            typeof(LocalHostInventoryResultMapping).GetProperties(),
            property => property.PropertyType == typeof(string));
    }

    [Theory]
    [InlineData(ExecutionOutcome.Failed, JobStatus.Failed)]
    [InlineData(ExecutionOutcome.Cancelled, JobStatus.Cancelled)]
    [InlineData(ExecutionOutcome.TimedOut, JobStatus.TimedOut)]
    [InlineData(ExecutionOutcome.Blocked, JobStatus.Blocked)]
    [InlineData(ExecutionOutcome.NotRun, JobStatus.NotRun)]
    public void LeasedDryRunFailureResolvesLease(
        ExecutionOutcome outcome,
        JobStatus expectedStatus)
    {
        var definition = LocalHostInventoryPackageMetadata.CreateDefinition(Time);
        var version = Assert.Single(definition.Versions);
        var job = QueuedJob(definition, version);
        var workerId = WorkerNodeId.New();
        var credentials = job.AcquireWorkLease(
            JobLeaseId.New(),
            workerId,
            JobWorkKind.DryRun,
            41,
            new UserIdentity($"worker:{workerId}"),
            Time.AddMinutes(4),
            Time.AddMinutes(10)).Credentials;

        job.TerminateDryRun(
            credentials,
            outcome,
            new UserIdentity($"worker:{workerId}"),
            Time.AddMinutes(5));

        Assert.Equal(expectedStatus, job.Status);
        Assert.Null(job.Lease);
    }

    [Fact]
    public async Task PackageRegistrationIsTransactionalAndIdempotent()
    {
        var scripts = new RecordingScriptRepository();
        var audits = new RecordingAuditWriter();
        var unitOfWork = new RecordingUnitOfWork();
        using var provider = RegistrationProvider(
            scripts,
            audits,
            unitOfWork);
        var registrar = new LocalHostInventoryPackageRegistrar(
            provider.GetRequiredService<IServiceScopeFactory>());

        Assert.True(await registrar.RegisterAsync(CancellationToken.None));
        Assert.False(await registrar.RegisterAsync(CancellationToken.None));

        var definition = Assert.IsType<ScriptDefinition>(scripts.Definition);
        LocalHostInventoryArtifactCatalog.ValidatePackage(
            definition,
            Assert.Single(definition.Versions));
        Assert.Equal(1, unitOfWork.CommitCount);
        var audit = Assert.Single(audits.Events);
        Assert.DoesNotContain(
            audit.Properties,
            property =>
                property.Key.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageRegistrationReloadsAfterConcurrentInsertConflict()
    {
        var scripts = new RecordingScriptRepository();
        var audits = new RecordingAuditWriter();
        var unitOfWork = new ConflictOnceUnitOfWork();
        using var provider = RegistrationProvider(
            scripts,
            audits,
            unitOfWork);
        var registrar = new LocalHostInventoryPackageRegistrar(
            provider.GetRequiredService<IServiceScopeFactory>());

        Assert.False(await registrar.RegisterAsync(CancellationToken.None));
        Assert.Equal(1, unitOfWork.CommitCount);
        var definition = Assert.IsType<ScriptDefinition>(scripts.Definition);
        LocalHostInventoryArtifactCatalog.ValidatePackage(
            definition,
            Assert.Single(definition.Versions));
    }

    private static ServiceProvider RegistrationProvider(
        RecordingScriptRepository scripts,
        RecordingAuditWriter audits,
        IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IScriptDefinitionRepository>(scripts);
        services.AddSingleton<IAuditWriter>(audits);
        services.AddSingleton(unitOfWork);
        services.AddSingleton<IWorkerCoordinationClock>(
            new FixedCoordinationClock(Time));
        return services.BuildServiceProvider();
    }

    private static Job SubmittedJob(
        ScriptDefinition definition,
        ScriptVersion version)
    {
        var requester = new UserIdentity("DOMAIN\\requester");
        var job = Job.CreateDraft(
            JobId.New(),
            definition.Id,
            version.Id,
            ExecutionPhase.DryRun,
            requester,
            Time);
        job.AddTarget(new TargetName("local-worker"), requester, Time);
        job.Submit(definition, requester, Time);
        return job;
    }

    private static Job QueuedJob(
        ScriptDefinition definition,
        ScriptVersion version)
    {
        var job = SubmittedJob(definition, version);
        var actor = new UserIdentity("system:test");
        job.MarkValidated(actor, Time.AddMinutes(2));
        job.QueueDryRun(actor, Time.AddMinutes(3));
        return job;
    }

    private static ScriptDefinition DefinitionWithVersion(
        ScriptVersion version,
        string? packageId = null)
    {
        var definition = ScriptDefinition.Create(
            LocalHostInventoryPackageMetadata.DefinitionId,
            new ScriptName(packageId ?? LocalHostInventoryPackageMetadata.PackageId),
            LocalHostInventoryPackageMetadata.DisplayName,
            LocalHostInventoryPackageMetadata.Description,
            RiskLevel.ReadOnly,
            new UserIdentity(LocalHostInventoryPackageMetadata.RegistrationActor),
            Time);
        definition.AddVersion(version, Time);
        return definition;
    }

    private static ScriptVersion Version(
        string? sha256 = null,
        ScriptParameterDefinition? parameter = null)
    {
        var version = new ScriptVersion(
            LocalHostInventoryPackageMetadata.VersionId,
            ScriptVersionNumber.Parse(LocalHostInventoryPackageMetadata.PackageVersion),
            LocalHostInventoryPackageMetadata.RelativeScriptPath,
            sha256 ?? LocalHostInventoryPackageMetadata.Sha256,
            null,
            LocalHostInventoryPackageMetadata.MinimumPowerShellVersion,
            LocalHostInventoryPackageMetadata.DefaultTimeoutMinutes,
            [ExecutionPhase.DryRun],
            [ReportFormat.Json],
            Time,
            new UserIdentity(LocalHostInventoryPackageMetadata.RegistrationActor));
        if (parameter is not null)
        {
            version.AddParameterDefinition(parameter);
        }

        version.Publish();
        return version;
    }

    private sealed class RecordingScriptRepository : IScriptDefinitionRepository
    {
        internal ScriptDefinition? Definition { get; private set; }

        public Task<ScriptDefinition?> GetByIdAsync(
            ScriptDefinitionId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Definition?.Id == id
                    ? Definition
                    : null);

        public Task AddAsync(
            ScriptDefinition definition,
            CancellationToken cancellationToken)
        {
            Definition = definition;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            ScriptDefinition definition,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        internal List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        internal int CommitCount { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            CommitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ConflictOnceUnitOfWork : IUnitOfWork
    {
        internal int CommitCount { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitCount++;
            throw new ApplicationConflictException(
                "A concurrent package registration committed first.");
        }
    }

    private sealed class FixedCoordinationClock(DateTimeOffset utcNow) :
        IWorkerCoordinationClock
    {
        public Task<DateTimeOffset> GetUtcNowAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(utcNow);
    }
}
