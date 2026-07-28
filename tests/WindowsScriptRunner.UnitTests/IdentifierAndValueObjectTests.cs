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
    public void IdentifierEqualityAndStringRepresentationAreStable()
    {
        var value = Guid.Parse("ec10ed64-ff1f-4a06-a2e1-fc3710061471");
        var first = new JobId(value);
        var second = new JobId(value);

        Assert.Equal(first, second);
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
