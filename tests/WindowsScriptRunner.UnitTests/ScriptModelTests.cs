using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Scripts;

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
        _ = TestDomainFactory.Parameter(
            "Mode",
            ScriptParameterType.Enum,
            allowedValues: ["Safe", "Fast"]);
        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter("Mode", ScriptParameterType.Enum));
        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter(
                "Credential",
                ScriptParameterType.SecureReference,
                sensitive: false));
    }

    [Theory]
    [InlineData("invalid-name")]
    [InlineData("1StartsWithNumber")]
    [InlineData("has space")]
    public void InvalidParameterIdentifiersAreRejected(string name) =>
        Assert.Throws<InvalidParameterDefinitionException>(() => TestDomainFactory.Parameter(name));
}
