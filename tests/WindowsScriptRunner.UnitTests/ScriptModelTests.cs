using System.Text.Json;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
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
    public void ScriptDefinitionUpdateDetailsAppliesAllValuesAtomically()
    {
        var script = TestDomainFactory.Script(TestDomainFactory.Version());
        var updatedUtc = TestDomainFactory.Time.AddMinutes(1);

        script.UpdateDetails(" Updated Script ", " Updated description ", updatedUtc);

        Assert.Equal("Updated Script", script.DisplayName);
        Assert.Equal("Updated description", script.Description);
        Assert.Equal(updatedUtc, script.UpdatedUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void InvalidDisplayNameLeavesEntireScriptDefinitionUnchanged(string? displayName)
    {
        var script = TestDomainFactory.Script(TestDomainFactory.Version());
        script.Disable(TestDomainFactory.Time.AddMinutes(1));
        var before = Capture(script);

        Assert.Throws<DomainValidationException>(
            () => script.UpdateDetails(
                displayName!,
                "Otherwise valid",
                before.UpdatedUtc.AddMinutes(1)));

        AssertUnchanged(script, before);
    }

    [Fact]
    public void OversizedDisplayNameLeavesEntireScriptDefinitionUnchanged()
    {
        var script = TestDomainFactory.Script(TestDomainFactory.Version(), RiskLevel.High);
        var before = Capture(script);

        Assert.Throws<DomainValidationException>(
            () => script.UpdateDetails(
                new string('d', 201),
                "Otherwise valid",
                before.UpdatedUtc.AddMinutes(1)));

        AssertUnchanged(script, before);
    }

    [Fact]
    public void InvalidDescriptionAfterValidDisplayNameLeavesEntireScriptDefinitionUnchanged()
    {
        var version = TestDomainFactory.Version();
        var script = TestDomainFactory.Script(version, RiskLevel.Critical);
        script.Disable(TestDomainFactory.Time.AddMinutes(1));
        var before = Capture(script);

        Assert.Throws<DomainValidationException>(
            () => script.UpdateDetails(
                "This valid name must not be assigned",
                new string('d', 2001),
                before.UpdatedUtc.AddMinutes(1)));

        AssertUnchanged(script, before);
    }

    [Fact]
    public void NullDescriptionAtRuntimeRemainsAValidOptionalValue()
    {
        var script = TestDomainFactory.Script(TestDomainFactory.Version());
        var updatedUtc = TestDomainFactory.Time.AddMinutes(1);

        script.UpdateDetails("Updated Script", null!, updatedUtc);

        Assert.Equal("Updated Script", script.DisplayName);
        Assert.Equal(string.Empty, script.Description);
        Assert.Equal(updatedUtc, script.UpdatedUtc);
    }

    [Fact]
    public void BackwardUpdateDetailsTimestampLeavesEntireScriptDefinitionUnchanged()
    {
        var script = TestDomainFactory.Script(TestDomainFactory.Version());
        script.Disable(TestDomainFactory.Time.AddMinutes(1));
        var before = Capture(script);

        Assert.Throws<DomainValidationException>(
            () => script.UpdateDetails(
                "Updated Script",
                "Updated description",
                before.UpdatedUtc.AddTicks(-1)));

        AssertUnchanged(script, before);
    }

    [Fact]
    public void ValidScriptDefinitionUpdateSucceedsAfterFailedAttempt()
    {
        var script = TestDomainFactory.Script(TestDomainFactory.Version());
        var before = Capture(script);

        Assert.Throws<DomainValidationException>(
            () => script.UpdateDetails(
                "Not applied",
                new string('d', 2001),
                before.UpdatedUtc.AddMinutes(1)));
        AssertUnchanged(script, before);

        var updatedUtc = before.UpdatedUtc.AddMinutes(2);
        script.UpdateDetails("Applied", "Valid description", updatedUtc);

        Assert.Equal("Applied", script.DisplayName);
        Assert.Equal("Valid description", script.Description);
        Assert.Equal(updatedUtc, script.UpdatedUtc);
        Assert.Equal(before.IsEnabled, script.IsEnabled);
        Assert.Equal(before.Versions, script.Versions);
        Assert.Equal(before.RiskLevel, script.RiskLevel);
        Assert.Equal(before.CreatedUtc, script.CreatedUtc);
        Assert.Equal(before.CreatedBy, script.CreatedBy);
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
    public void ExecuteCapableVersionRequiresDryRunBeforePublication()
    {
        var executeOnly = TestDomainFactory.Version(
            publish: false,
            phases: [ExecutionPhase.Execute]);

        Assert.Throws<InvalidScriptVersionException>(() => executeOnly.Publish());

        Assert.False(executeOnly.IsPublished);
    }

    [Theory]
    [InlineData(ExecutionPhase.DryRun)]
    [InlineData(ExecutionPhase.Validation)]
    public void NonExecuteVersionsCanPublishWithoutExecute(ExecutionPhase phase)
    {
        var version = TestDomainFactory.Version(
            publish: false,
            phases: [phase]);

        version.Publish();

        Assert.True(version.IsPublished);
    }

    [Fact]
    public void DryRunAndExecuteVersionCanPublish()
    {
        var version = TestDomainFactory.Version(
            publish: false,
            phases: [ExecutionPhase.DryRun, ExecutionPhase.Execute]);

        version.Publish();

        Assert.True(version.IsPublished);
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
    public void DuplicateParameterDefinitionIdentifierIsRejectedWithoutMutation()
    {
        var version = TestDomainFactory.Version(publish: false);
        var identifier = ScriptParameterDefinitionId.New();
        var first = new ScriptParameterDefinition(
            identifier,
            "Mode",
            "Mode",
            null,
            ScriptParameterType.String,
            false,
            null,
            [],
            false);
        var duplicate = new ScriptParameterDefinition(
            identifier,
            "Region",
            "Region",
            null,
            ScriptParameterType.String,
            false,
            null,
            [],
            false);
        version.AddParameterDefinition(first);

        Assert.Throws<InvalidParameterDefinitionException>(
            () => version.AddParameterDefinition(duplicate));

        Assert.Same(first, Assert.Single(version.ParameterDefinitions));
    }

    [Fact]
    public void TypedDefaultsAreValidated()
    {
        var text = TestDomainFactory.Parameter(
            "Text",
            defaultValue: "  preserved value  ");
        _ = TestDomainFactory.Parameter("Enabled", ScriptParameterType.Boolean, defaultValue: "true");
        _ = TestDomainFactory.Parameter("Count", ScriptParameterType.Integer, defaultValue: "42");
        _ = TestDomainFactory.Parameter(
            "When",
            ScriptParameterType.DateTime,
            defaultValue: "2026-07-28T12:00:00+00:00");

        Assert.Equal("  preserved value  ", text.DefaultValue);
        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter("Enabled", ScriptParameterType.Boolean, defaultValue: "yes"));
        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter("Count", ScriptParameterType.Integer, defaultValue: "4.2"));
        Assert.Throws<InvalidParameterDefinitionException>(
            () => TestDomainFactory.Parameter("When", ScriptParameterType.DateTime, defaultValue: "tomorrow"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void BlankParameterDefaultsAreCanonicalAbsence(string defaultValue)
    {
        var definition = TestDomainFactory.Parameter(
            "Mode",
            required: true,
            defaultValue: defaultValue);

        Assert.Null(definition.DefaultValue);
        Assert.Throws<InvalidJobParameterException>(
            () => definition.ValidateSerializedValue(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void BlankGitCommitShaIsCanonicalAbsence(string gitCommitSha)
    {
        var version = new ScriptVersion(
            ScriptVersionId.New(),
            ScriptVersionNumber.Parse("1.0.0"),
            "scripts/Test.ps1",
            new string('a', 64),
            gitCommitSha,
            "7.4",
            30,
            [ExecutionPhase.DryRun],
            [],
            TestDomainFactory.Time,
            TestDomainFactory.User);

        Assert.Null(version.GitCommitSha);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[\"server-01\",null]")]
    public void StringArraysRejectNullElements(string serializedValue)
    {
        var definition = TestDomainFactory.Parameter(
            "Targets",
            ScriptParameterType.StringArray);

        Assert.Throws<InvalidJobParameterException>(
            () => definition.ValidateSerializedValue(serializedValue));
    }

    [Fact]
    public void SerializedValuesRejectOversizedInput()
    {
        var definition = TestDomainFactory.Parameter("Text");

        Assert.Throws<InvalidJobParameterException>(
            () => definition.ValidateSerializedValue(new string('a', 4001)));
    }

    [Fact]
    public void SerializedStringArraysRejectOversizedInput()
    {
        var definition = TestDomainFactory.Parameter(
            "Targets",
            ScriptParameterType.StringArray);
        var serializedValue = JsonSerializer.Serialize(new[] { new string('a', 4001) });

        Assert.Throws<InvalidJobParameterException>(
            () => definition.ValidateSerializedValue(serializedValue));
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

    [Theory]
    [InlineData(RiskLevel.ReadOnly)]
    [InlineData(RiskLevel.Low)]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Critical)]
    public void DefinedRiskLevelsAreAccepted(RiskLevel riskLevel)
    {
        var script = TestDomainFactory.Script(riskLevel: riskLevel);

        Assert.Equal(riskLevel, script.RiskLevel);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedRiskLevelIsRejectedBeforeTrustedPolicyCapture(int riskLevel)
    {
        Assert.Throws<DomainValidationException>(
            () => TestDomainFactory.Script(riskLevel: (RiskLevel)riskLevel));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedExecutionPhaseIsRejectedByScriptVersion(int phase)
    {
        Assert.Throws<DomainValidationException>(
            () => TestDomainFactory.Version(
                publish: false,
                phases: [(ExecutionPhase)phase]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedReportFormatIsRejectedByScriptVersion(int reportFormat)
    {
        Assert.Throws<DomainValidationException>(
            () => new ScriptVersion(
                ScriptVersionId.New(),
                ScriptVersionNumber.Parse("1.0.0"),
                "scripts/Test.ps1",
                new string('a', 64),
                null,
                "7.4",
                30,
                [ExecutionPhase.Validation],
                [(ReportFormat)reportFormat],
                TestDomainFactory.Time,
                TestDomainFactory.User));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedScriptParameterTypeIsRejectedByDefinitions(int parameterType)
    {
        Assert.Throws<DomainValidationException>(
            () => TestDomainFactory.Parameter(type: (ScriptParameterType)parameterType));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedApprovalDecisionIsRejected(int decision)
    {
        Assert.Throws<DomainValidationException>(
            () => new JobApproval(
                JobApprovalId.New(),
                (ApprovalDecision)decision,
                TestDomainFactory.OtherUser,
                TestDomainFactory.Time,
                null,
                TestDomainFactory.Fingerprint));
    }

    private static ScriptDefinitionState Capture(ScriptDefinition script) =>
        new(
            script.Id,
            script.Name,
            script.DisplayName,
            script.Description,
            script.RiskLevel,
            script.IsEnabled,
            script.CreatedBy,
            script.CreatedUtc,
            script.UpdatedUtc,
            script.Versions.ToArray());

    private static void AssertUnchanged(
        ScriptDefinition script,
        ScriptDefinitionState before)
    {
        Assert.Equal(before.Id, script.Id);
        Assert.Equal(before.Name, script.Name);
        Assert.Equal(before.DisplayName, script.DisplayName);
        Assert.Equal(before.Description, script.Description);
        Assert.Equal(before.RiskLevel, script.RiskLevel);
        Assert.Equal(before.IsEnabled, script.IsEnabled);
        Assert.Equal(before.CreatedBy, script.CreatedBy);
        Assert.Equal(before.CreatedUtc, script.CreatedUtc);
        Assert.Equal(before.UpdatedUtc, script.UpdatedUtc);
        Assert.Equal(before.Versions, script.Versions);
    }

    private sealed record ScriptDefinitionState(
        ScriptDefinitionId Id,
        ScriptName Name,
        string DisplayName,
        string Description,
        RiskLevel RiskLevel,
        bool IsEnabled,
        UserIdentity CreatedBy,
        DateTimeOffset CreatedUtc,
        DateTimeOffset UpdatedUtc,
        ScriptVersion[] Versions);
}
