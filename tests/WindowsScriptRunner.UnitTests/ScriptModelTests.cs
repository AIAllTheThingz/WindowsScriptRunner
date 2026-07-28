using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.UnitTests;

public sealed class ScriptModelTests
{
    [Fact]
    public void ScriptDefinitionCanAddVersionAndToggleAvailability()
    {
        var script = TestDomainFactory.Script();
        var version = TestDomainFactory.Version();

        script.AddVersion(version, TestDomainFactory.Time.AddMinutes(1));
        script.Disable(TestDomainFactory.Time.AddMinutes(2));
        Assert.False(script.IsEnabled);
        script.Enable(TestDomainFactory.Time.AddMinutes(3));

        Assert.True(script.IsEnabled);
        Assert.Same(version, Assert.Single(script.Versions));
    }

    [Fact]
    public void DuplicateVersionNumberIsRejected()
    {
        var script = TestDomainFactory.Script(TestDomainFactory.Version());
        var duplicate = TestDomainFactory.Version();

        Assert.Throws<InvalidScriptVersionException>(
            () => script.AddVersion(duplicate, TestDomainFactory.Time.AddMinutes(1)));
    }

    [Fact]
    public void DuplicateVersionIdentifierIsRejectedWithoutMutation()
    {
        var existing = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(existing);
        var originalUpdatedUtc = script.UpdatedUtc;
        var duplicateId = new ScriptVersion(
            existing.Id,
            ScriptVersionNumber.Parse("2.0.0"),
            "scripts/Other.ps1",
            new string('c', 64),
            null,
            "7.4",
            30,
            [ExecutionPhase.DryRun],
            [],
            TestDomainFactory.Time,
            TestDomainFactory.User);

        Assert.Throws<InvalidScriptVersionException>(
            () => script.AddVersion(duplicateId, TestDomainFactory.Time.AddMinutes(1)));

        Assert.Single(script.Versions);
        Assert.Equal(originalUpdatedUtc, script.UpdatedUtc);
        Assert.Same(existing, script.GetVersion(existing.Id));
    }

    [Fact]
    public void DifferentVersionIdentifierAndNumberAreAccepted()
    {
        var script = TestDomainFactory.Script(TestDomainFactory.Version());
        var next = new ScriptVersion(
            ScriptVersionId.New(),
            ScriptVersionNumber.Parse("1.1.0"),
            "scripts/Next.ps1",
            new string('d', 64),
            null,
            "7.4",
            30,
            [ExecutionPhase.DryRun],
            [],
            TestDomainFactory.Time,
            TestDomainFactory.User);

        script.AddVersion(next, TestDomainFactory.Time.AddMinutes(1));

        Assert.Equal(2, script.Versions.Count);
        Assert.Same(next, script.GetVersion(next.Id));
    }

    [Fact]
    public void PublishedVersionRejectsParameterMutation()
    {
        var version = TestDomainFactory.Version();

        Assert.Throws<InvalidScriptVersionException>(
            () => version.AddParameterDefinition(TestDomainFactory.Parameter()));
    }

    [Fact]
    public void DuplicateParameterNameIsRejectedCaseInsensitively()
    {
        var version = TestDomainFactory.Version(publish: false);
        version.AddParameterDefinition(TestDomainFactory.Parameter("Mode"));

        Assert.Throws<InvalidParameterDefinitionException>(
            () => version.AddParameterDefinition(TestDomainFactory.Parameter("mode")));
    }

    [Fact]
    public void TypedDefaultsAreValidated()
    {
        _ = TestDomainFactory.Parameter("Enabled", ScriptParameterType.Boolean, defaultValue: "true");
        _ = TestDomainFactory.Parameter("Count", ScriptParameterType.Integer, defaultValue: "42");
        _ = TestDomainFactory.Parameter(
            "When",
            ScriptParameterType.DateTime,
            defaultValue: "2026-07-28T12:00:00+00:00");

        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter("Enabled", ScriptParameterType.Boolean, defaultValue: "yes"));
        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter("Count", ScriptParameterType.Integer, defaultValue: "4.2"));
        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter("When", ScriptParameterType.DateTime, defaultValue: "tomorrow"));
    }

    [Fact]
    public void EnumAndSecureReferenceRulesAreEnforced()
    {
        var credentialReferenceId = CredentialReferenceId.New().ToString();
        _ = TestDomainFactory.Parameter(
            "Mode",
            ScriptParameterType.Enum,
            allowedValues: ["Safe", "Fast"]);
        var secureReference = TestDomainFactory.Parameter(
            "Credential",
            ScriptParameterType.SecureReference,
            sensitive: true);
        secureReference.ValidateSerializedValue(credentialReferenceId);

        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter("Mode", ScriptParameterType.Enum));
        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter(
                "Credential",
                ScriptParameterType.SecureReference,
                sensitive: false));
        Assert.Throws<InvalidJobParameterException>(
            () => secureReference.ValidateSerializedValue("hunter2"));
        Assert.Throws<InvalidJobParameterException>(
            () => secureReference.ValidateSerializedValue(Guid.Empty.ToString("D")));
        Assert.Throws<InvalidJobParameterException>(
            () => secureReference.ValidateSerializedValue("not-a-guid"));
        Assert.Throws<InvalidJobParameterException>(
            () => secureReference.ValidateSerializedValue(credentialReferenceId.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("invalid-name")]
    [InlineData("1StartsWithNumber")]
    [InlineData("has space")]
    public void InvalidParameterIdentifiersAreRejected(string name) =>
        Assert.Throws<InvalidParameterDefinitionException>(() => TestDomainFactory.Parameter(name));
}
