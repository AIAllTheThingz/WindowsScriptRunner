using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.UnitTests;

internal static class TestDomainFactory
{
    public static readonly DateTimeOffset Time = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
    public static readonly UserIdentity User = new("DOMAIN\\requester");
    public static readonly UserIdentity OtherUser = new("DOMAIN\\approver");
    public static readonly string Fingerprint = new('b', 64);

    public static ScriptParameterDefinition Parameter(
        string name = "Mode",
        ScriptParameterType type = ScriptParameterType.String,
        bool required = false,
        string? defaultValue = null,
        IEnumerable<string>? allowedValues = null,
        bool sensitive = false) =>
        new(
            ScriptParameterDefinitionId.New(),
            name,
            name,
            null,
            type,
            required,
            defaultValue,
            allowedValues,
            sensitive);

    public static ScriptVersion Version(
        IEnumerable<ScriptParameterDefinition>? parameters = null,
        bool publish = true,
        IEnumerable<ExecutionPhase>? phases = null)
    {
        var version = new ScriptVersion(
            ScriptVersionId.New(),
            ScriptVersionNumber.Parse("1.0.0"),
            "scripts/Test.ps1",
            new string('a', 64),
            "abcdef1",
            "7.4",
            30,
            phases ?? [ExecutionPhase.DryRun, ExecutionPhase.Execute],
            [ReportFormat.Json],
            Time,
            User);
        foreach (var parameter in parameters ?? [])
        {
            version.AddParameterDefinition(parameter);
        }

        if (publish)
        {
            version.Publish();
        }

        return version;
    }

    public static ScriptDefinition Script(
        ScriptVersion? version = null,
        RiskLevel riskLevel = RiskLevel.Low)
    {
        var script = ScriptDefinition.Create(
            ScriptDefinitionId.New(),
            new ScriptName("test.script"),
            "Test Script",
            "Test description",
            riskLevel,
            User,
            Time);
        if (version is not null)
        {
            script.AddVersion(version, Time);
        }

        return script;
    }

    public static Job DraftJob(
        ScriptDefinition script,
        ScriptVersion version,
        ExecutionPhase requestedPhase = ExecutionPhase.DryRun) =>
        Job.CreateDraft(
            JobId.New(),
            script.Id,
            version.Id,
            requestedPhase,
            User,
            Time);

    public static Job SubmittedJob(
        ScriptDefinition script,
        ScriptVersion version,
        IEnumerable<(ScriptParameterDefinition Definition, string? Value)>? parameters = null,
        ExecutionPhase requestedPhase = ExecutionPhase.DryRun)
    {
        var job = DraftJob(script, version, requestedPhase);
        job.AddTarget(new TargetName("server-01"), User, Time.AddMinutes(1));
        foreach (var parameter in parameters ?? [])
        {
            job.SetParameter(parameter.Definition, parameter.Value, User, Time.AddMinutes(2));
        }

        job.Submit(script, User, Time.AddMinutes(3));
        return job;
    }

    public static void AdvanceToAwaitingApproval(Job job)
    {
        var time = job.UpdatedUtc;
        job.MarkValidated(OtherUser, time = time.AddMinutes(1));
        job.QueueDryRun(OtherUser, time = time.AddMinutes(1));
        job.StartDryRun(OtherUser, time = time.AddMinutes(1));
        job.CompleteDryRun(OtherUser, time = time.AddMinutes(1));
        job.RequireApproval(OtherUser, time.AddMinutes(1));
    }
}
