using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.UnitTests;

public sealed class ApprovalFingerprintServiceTests
{
    [Fact]
    public async Task FingerprintHasFixedLowercaseSha256Vector()
    {
        var fixture = CreateFixture();

        var fingerprint = await fixture.Service.CreateFingerprintAsync(
            fixture.Job,
            CancellationToken.None);

        Assert.Equal(
            "d81f1d0193bd91c3f834fea5bfa025741ff7db0162231e6e814ac038b07c199c",
            fingerprint);
        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }

    [Fact]
    public async Task CanonicalizationMakesPersistedCollectionOrderIrrelevant()
    {
        var ordered = CreateFixture(reverseCollections: false);
        var reordered = CreateFixture(reverseCollections: true);

        var orderedFingerprint = await ordered.Service.CreateFingerprintAsync(
            ordered.Job,
            CancellationToken.None);
        var reorderedFingerprint = await reordered.Service.CreateFingerprintAsync(
            reordered.Job,
            CancellationToken.None);

        Assert.Equal(orderedFingerprint, reorderedFingerprint);
    }

    [Fact]
    public async Task FingerprintChangesWhenTrustedScriptTargetsParametersOrEvidenceChange()
    {
        var baseline = CreateFixture();
        var changedScript = CreateFixture(scriptSha256: new string('f', 64));
        var changedTarget = CreateFixture(secondTargetName: "server-c");
        var changedParameter = CreateFixture(parameterValue: "Enforced");
        var changedEvidenceWindow = CreateFixture(evidenceCompletedUtc: FixedTime.AddMinutes(10));

        var baselineFingerprint = await baseline.Service.CreateFingerprintAsync(
            baseline.Job,
            CancellationToken.None);
        var scriptFingerprint = await changedScript.Service.CreateFingerprintAsync(
            changedScript.Job,
            CancellationToken.None);
        var targetFingerprint = await changedTarget.Service.CreateFingerprintAsync(
            changedTarget.Job,
            CancellationToken.None);
        var parameterFingerprint = await changedParameter.Service.CreateFingerprintAsync(
            changedParameter.Job,
            CancellationToken.None);
        var evidenceFingerprint = await changedEvidenceWindow.Service.CreateFingerprintAsync(
            changedEvidenceWindow.Job,
            CancellationToken.None);

        Assert.NotEqual(baselineFingerprint, scriptFingerprint);
        Assert.NotEqual(baselineFingerprint, targetFingerprint);
        Assert.NotEqual(baselineFingerprint, parameterFingerprint);
        Assert.NotEqual(baselineFingerprint, evidenceFingerprint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
    [InlineData("bbbb")]
    public void ExpectedFingerprintComparisonRejectsMalformedAndStaleValues(string? expected)
    {
        var current = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        Assert.False(ApprovalFingerprintService.IsExpectedFingerprintCurrent(expected, current));
    }

    [Fact]
    public void ExpectedFingerprintComparisonAcceptsOnlyTheExactLowercaseSha256Value()
    {
        var fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        Assert.True(ApprovalFingerprintService.IsExpectedFingerprintCurrent(fingerprint, fingerprint));
        Assert.False(ApprovalFingerprintService.IsExpectedFingerprintCurrent(
            fingerprint.ToUpperInvariant(),
            fingerprint));

        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WindowsScriptRunner.Application",
            "Jobs",
            "ApprovalFingerprintService.cs"));
        Assert.Contains("CryptographicOperations.FixedTimeEquals", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FingerprintFailsWhenCurrentAcceptedDryRunEvidenceIsAbsent()
    {
        var fixture = CreateFixture(evidenceOutcome: ExecutionOutcome.Failed);

        await Assert.ThrowsAsync<ApplicationConflictException>(
            () => fixture.Service.CreateFingerprintAsync(fixture.Job, CancellationToken.None));
    }

    [Fact]
    public void FingerprintPreimageHasNoLoggingSurface()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WindowsScriptRunner.Application",
            "Jobs",
            "ApprovalFingerprintService.cs"));

        Assert.DoesNotContain("ILogger", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LogInformation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LogDebug", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LogTrace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LogWarning", source, StringComparison.Ordinal);
    }

    private static readonly DateTimeOffset FixedTime = new(2026, 8, 1, 12, 30, 0, TimeSpan.Zero);
    private static readonly UserIdentity Requester = new("sid:S-1-5-21-100-200-300-400");
    private static readonly UserIdentity Reviewer = new("sid:S-1-5-21-100-200-300-401");
    private static readonly ScriptDefinitionId DefinitionId = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly ScriptVersionId VersionId = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static readonly JobId JobId = new(
        Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly JobExecutionId ExecutionId = new(
        Guid.Parse("44444444-4444-4444-4444-444444444444"));
    private static readonly WorkerNodeId WorkerId = new(
        Guid.Parse("55555555-5555-5555-5555-555555555555"));

    private static FingerprintFixture CreateFixture(
        bool reverseCollections = false,
        string parameterValue = "Audit",
        string scriptSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        string secondTargetName = "Server-A",
        DateTimeOffset? evidenceCompletedUtc = null,
        ExecutionOutcome evidenceOutcome = ExecutionOutcome.Succeeded)
    {
        var version = new ScriptVersion(
            VersionId,
            ScriptVersionNumber.Parse("8.9.10"),
            "scripts/LocalHostInventory.ps1",
            scriptSha256,
            "a1b2c3d4",
            "7.4.2",
            45,
            [ExecutionPhase.Validation, ExecutionPhase.DryRun, ExecutionPhase.Execute],
            [ReportFormat.Json],
            FixedTime,
            Requester);
        version.Publish();
        var definition = ScriptDefinition.Create(
            DefinitionId,
            new ScriptName("local.host.inventory"),
            "Local Host Inventory",
            "Collects the local host inventory.",
            RiskLevel.High,
            Requester,
            FixedTime);
        definition.AddVersion(version, FixedTime);

        var targets = new[]
        {
            new JobTarget(new TargetName("server-b"), FixedTime.AddMinutes(1), Requester),
            new JobTarget(new TargetName(secondTargetName), FixedTime.AddMinutes(2), Requester),
        };
        var parameters = new[]
        {
            new JobParameter("Zone", "west"),
            new JobParameter("Mode", parameterValue),
        };
        var completedUtc = evidenceCompletedUtc ?? FixedTime.AddMinutes(9);
        var execution = JobExecution.Rehydrate(
            ExecutionId,
            1,
            WorkerId,
            FixedTime.AddMinutes(7),
            FixedTime.AddMinutes(8),
            completedUtc,
            evidenceOutcome,
            evidenceOutcome == ExecutionOutcome.Failed ? 1 : 0,
            "Validated dry run.");
        var job = Job.Rehydrate(
            JobId,
            DefinitionId,
            VersionId,
            ExecutionPhase.Execute,
            JobStatus.AwaitingApproval,
            Requester,
            Reviewer,
            FixedTime,
            FixedTime.AddMinutes(10),
            FixedTime.AddMinutes(5),
            "Inventory requested for maintenance review.",
            new ChangeReference("CHG-2026-008"),
            JobPolicySnapshot.Rehydrate(
                DefinitionId,
                VersionId,
                RiskLevel.High,
                supportsExecutePhase: true,
                supportsPostValidationPhase: false),
            reverseCollections ? targets.Reverse() : targets,
            reverseCollections ? parameters.Reverse() : parameters,
            [execution],
            []);

        return new FingerprintFixture(
            job,
            new ApprovalFingerprintService(new FixedScriptRepository(definition)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WindowsScriptRunner.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test execution directory.");
    }

    private sealed record FingerprintFixture(Job Job, ApprovalFingerprintService Service);

    private sealed class FixedScriptRepository(ScriptDefinition definition) : IScriptDefinitionRepository
    {
        public Task<ScriptDefinition?> GetByIdAsync(
            ScriptDefinitionId id,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScriptDefinition?>(definition.Id == id ? definition : null);

        public Task AddAsync(ScriptDefinition scriptDefinition, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(ScriptDefinition scriptDefinition, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
