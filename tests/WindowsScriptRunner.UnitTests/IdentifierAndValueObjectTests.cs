using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.UnitTests;

public sealed class IdentifierAndValueObjectTests
{
    [Fact]
    public void StronglyTypedIdentifiersCreateNonEmptyValues()
    {
        Guid[] values =
        [
            ScriptDefinitionId.New().Value,
            ScriptVersionId.New().Value,
            ScriptParameterDefinitionId.New().Value,
            JobId.New().Value,
            JobExecutionId.New().Value,
            JobApprovalId.New().Value,
            JobLogEntryId.New().Value,
            JobReportId.New().Value,
            WorkerNodeId.New().Value,
            AuditEventId.New().Value,
            CredentialReferenceId.New().Value,
        ];

        Assert.All(values, value => Assert.NotEqual(Guid.Empty, value));
    }

    [Fact]
    public void EmptyIdentifierIsRejected() =>
        Assert.Throws<DomainValidationException>(() => new JobId(Guid.Empty));

    [Fact]
    public void EachIdentifierRejectsEmptyGuidAndAcceptsValidGuid()
    {
        var valid = Guid.Parse("0bb66c1e-038d-4350-bb2d-cff8b9cb43f9");
        Action[] rejected =
        [
            () => new ScriptDefinitionId(Guid.Empty),
            () => new ScriptVersionId(Guid.Empty),
            () => new ScriptParameterDefinitionId(Guid.Empty),
            () => new JobId(Guid.Empty),
            () => new JobExecutionId(Guid.Empty),
            () => new JobApprovalId(Guid.Empty),
            () => new JobLogEntryId(Guid.Empty),
            () => new JobReportId(Guid.Empty),
            () => new WorkerNodeId(Guid.Empty),
            () => new AuditEventId(Guid.Empty),
            () => new CredentialReferenceId(Guid.Empty),
        ];

        foreach (var action in rejected)
        {
            Assert.Throws<DomainValidationException>(action);
        }

        Assert.Equal(valid, new ScriptDefinitionId(valid).Value);
        Assert.Equal(valid, new ScriptVersionId(valid).Value);
        Assert.Equal(valid, new ScriptParameterDefinitionId(valid).Value);
        Assert.Equal(valid, new JobId(valid).Value);
        Assert.Equal(valid, new JobExecutionId(valid).Value);
        Assert.Equal(valid, new JobApprovalId(valid).Value);
        Assert.Equal(valid, new JobLogEntryId(valid).Value);
        Assert.Equal(valid, new JobReportId(valid).Value);
        Assert.Equal(valid, new WorkerNodeId(valid).Value);
        Assert.Equal(valid, new AuditEventId(valid).Value);
        Assert.Equal(valid, new CredentialReferenceId(valid).Value);
    }

    [Fact]
    public void IdentifierEqualityAndStringRepresentationAreStable()
    {
        var value = Guid.Parse("ec10ed64-ff1f-4a06-a2e1-fc3710061471");
        var first = new JobId(value);
        var second = new JobId(value);
        object differentType = new ScriptVersionId(value);

        Assert.Equal(first, second);
        Assert.NotEqual(differentType, first);
        Assert.Equal("ec10ed64-ff1f-4a06-a2e1-fc3710061471", first.ToString());
    }

    [Theory]
    [InlineData("script-name")]
    [InlineData("Script_1.0")]
    public void ValidScriptNamesAreNormalized(string value) =>
        Assert.Equal(value, new ScriptName($" {value} ").Value);

    [Theory]
    [InlineData("ab")]
    [InlineData("../script")]
    [InlineData("folder/script")]
    [InlineData("script name")]
    public void InvalidScriptNamesAreRejected(string value) =>
        Assert.Throws<DomainValidationException>(() => new ScriptName(value));

    [Fact]
    public void SemanticVersionRequiresThreeNonNegativeComponents()
    {
        Assert.Equal("2.3.14", ScriptVersionNumber.Parse("2.3.14").ToString());
        Assert.Throws<DomainValidationException>(() => ScriptVersionNumber.Parse("2.3"));
        Assert.Throws<DomainValidationException>(() => ScriptVersionNumber.Parse("1.0.-1"));
        Assert.Throws<DomainValidationException>(() => ScriptVersionNumber.Parse("1.0.x"));
    }

    [Fact]
    public void UserIdentityRejectsControlCharacters()
    {
        Assert.Equal("DOMAIN\\user", new UserIdentity(" DOMAIN\\user ").Value);
        Assert.Throws<DomainValidationException>(() => new UserIdentity("bad\nuser"));
    }

    [Fact]
    public void UserIdentityUsesCaseInsensitiveWindowsEquality()
    {
        var first = new UserIdentity("DOMAIN\\User");
        var second = new UserIdentity("domain\\user");
        var third = new UserIdentity("DOMAIN\\USER");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(second, third);
        Assert.NotEqual(first, new UserIdentity("OTHERDOMAIN\\User"));
        Assert.NotEqual(first, new UserIdentity("DOMAIN\\OtherUser"));
        Assert.Equal("DOMAIN\\User", first.Value);
        Assert.Equal("DOMAIN\\User", first.ToString());
    }

    [Fact]
    public void TargetNameUsesCaseInsensitiveEquality()
    {
        Assert.Equal(new TargetName("SERVER-01"), new TargetName("server-01"));
        Assert.Throws<DomainValidationException>(() => new TargetName("server;whoami"));
    }

    [Fact]
    public void ScriptVersionRejectsTraversalAndMalformedHash()
    {
        Assert.Throws<InvalidScriptVersionException>(() => CreateVersion("../bad.ps1", new string('a', 64)));
        Assert.Throws<InvalidScriptVersionException>(() => CreateVersion("good.ps1", "not-a-hash"));
    }

    private static ScriptVersion CreateVersion(string path, string hash) =>
        new(
            ScriptVersionId.New(),
            ScriptVersionNumber.Parse("1.0.0"),
            path,
            hash,
            null,
            "7.4",
            30,
            [ExecutionPhase.Validation],
            [],
            TestDomainFactory.Time,
            TestDomainFactory.User);
}
