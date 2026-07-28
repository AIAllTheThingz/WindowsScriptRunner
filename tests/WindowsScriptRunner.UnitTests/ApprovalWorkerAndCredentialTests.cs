using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.UnitTests;

public sealed class ApprovalWorkerAndCredentialTests
{
    [Theory]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Critical)]
    public void RequesterCannotSelfApproveElevatedRisk(RiskLevel riskLevel)
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version, riskLevel),
            version);
        TestDomainFactory.AdvanceToAwaitingApproval(job);

        Assert.Throws<DomainValidationException>(
            () => job.RecordApproval(
                TestDomainFactory.User,
                TestDomainFactory.Fingerprint,
                null,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void ReadOnlyRequesterMaySelfApprove()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(
            TestDomainFactory.Script(version, RiskLevel.ReadOnly),
            version);
        TestDomainFactory.AdvanceToAwaitingApproval(job);

        job.RecordApproval(
            TestDomainFactory.User,
            TestDomainFactory.Fingerprint,
            null,
            job.UpdatedUtc.AddMinutes(1));

        Assert.Equal(JobStatus.Approved, job.Status);
    }

    [Fact]
    public void InvalidApprovalFingerprintIsRejected()
    {
        var version = TestDomainFactory.Version();
        var job = TestDomainFactory.SubmittedJob(TestDomainFactory.Script(version), version);
        TestDomainFactory.AdvanceToAwaitingApproval(job);

        Assert.Throws<DomainValidationException>(
            () => job.RecordApproval(
                TestDomainFactory.OtherUser,
                "invalid",
                null,
                job.UpdatedUtc.AddMinutes(1)));
    }

    [Fact]
    public void WorkerCapabilitiesHeartbeatAndAvailabilityAreProtected()
    {
        var worker = new WorkerNode(
            WorkerNodeId.New(),
            "worker-01",
            TestDomainFactory.Time);
        worker.RegisterCapability(new WorkerCapability("PowerShellVersion", "7.6"));
        Assert.Throws<DomainValidationException>(
            () => worker.RegisterCapability(new WorkerCapability("powershellversion", "7.5")));

        worker.RecordHeartbeat(TestDomainFactory.Time.AddMinutes(1));
        Assert.Throws<DomainValidationException>(
            () => worker.RecordHeartbeat(TestDomainFactory.Time));
        worker.Disable();
        Assert.False(worker.IsEnabled);
        worker.Enable();

        Assert.True(worker.IsEnabled);
    }

    [Fact]
    public void CredentialReferenceContainsNoSecretAndRedactsExternalIdentifier()
    {
        const string externalIdentifier = "vault/path/credential-1";
        var reference = new CredentialReference(
            CredentialReferenceId.New(),
            "ExternalVault",
            externalIdentifier,
            "Deployment Credential",
            TestDomainFactory.Time,
            TestDomainFactory.User);

        Assert.DoesNotContain(
            reference.GetType().GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(externalIdentifier, reference.ToString(), StringComparison.Ordinal);
        Assert.Throws<DomainValidationException>(
            () => new CredentialReference(
                CredentialReferenceId.New(),
                "ExternalVault",
                "password=actual-value",
                "Bad",
                TestDomainFactory.Time,
                TestDomainFactory.User));
    }
}
