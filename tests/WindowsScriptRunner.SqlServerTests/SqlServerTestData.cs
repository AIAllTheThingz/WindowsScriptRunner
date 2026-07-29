using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.Domain.Workers;

namespace WindowsScriptRunner.SqlServerTests;

internal static class SqlServerTestData
{
    public static readonly DateTimeOffset Time =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
    public static readonly UserIdentity Requester = new("DOMAIN\\requester");
    public static readonly UserIdentity Approver = new("DOMAIN\\approver");
    public static readonly string Fingerprint = new('b', 64);

    public static ScriptParameterDefinition Parameter(
        string name,
        ScriptParameterType type = ScriptParameterType.String,
        bool required = false,
        IEnumerable<string>? allowedValues = null,
        bool sensitive = false) =>
        new(
            ScriptParameterDefinitionId.New(),
            name,
            name,
            null,
            type,
            required,
            null,
            allowedValues,
            sensitive);

    public static ScriptVersion Version(
        IEnumerable<ScriptParameterDefinition>? parameters = null,
        IEnumerable<ExecutionPhase>? phases = null,
        string version = "1.0.0",
        bool publish = true)
    {
        var result = new ScriptVersion(
            ScriptVersionId.New(),
            ScriptVersionNumber.Parse(version),
            $"scripts/Test-{version}.ps1",
            new string('a', 64),
            "abcdef1",
            "7.4",
            30,
            phases ?? [ExecutionPhase.DryRun, ExecutionPhase.Execute, ExecutionPhase.PostValidation],
            [ReportFormat.Json],
            Time,
            Requester);
        foreach (var parameter in parameters ?? [])
        {
            result.AddParameterDefinition(parameter);
        }

        if (publish)
        {
            result.Publish();
        }

        return result;
    }

    public static ScriptDefinition Script(
        ScriptVersion version,
        RiskLevel riskLevel = RiskLevel.High,
        string name = "test.script")
    {
        var script = ScriptDefinition.Create(
            ScriptDefinitionId.New(),
            new ScriptName(name),
            "Test Script",
            "SQL Server integration test",
            riskLevel,
            Requester,
            Time);
        script.AddVersion(version, Time);
        return script;
    }

    public static Job DraftJob(
        ScriptDefinition script,
        ScriptVersion version,
        ExecutionPhase phase = ExecutionPhase.Execute) =>
        Job.CreateDraft(
            JobId.New(),
            script.Id,
            version.Id,
            phase,
            Requester,
            Time,
            "Persistence test",
            new ChangeReference("CHG000001"));

    public static Job SubmittedJob(
        ScriptDefinition script,
        ScriptVersion version,
        IEnumerable<(ScriptParameterDefinition Definition, string Value)>? parameters = null,
        ExecutionPhase phase = ExecutionPhase.Execute)
    {
        var job = DraftJob(script, version, phase);
        job.AddTarget(new TargetName("server-01"), Requester, Time.AddMinutes(1));
        foreach (var (definition, value) in parameters ?? [])
        {
            definition.ValidateSerializedValue(value);
            job.SetParameterValue(definition.Name, value, Requester, Time.AddMinutes(2));
        }

        job.Submit(script, Requester, Time.AddMinutes(3));
        return job;
    }

    public static Job CompleteExecuteJob(
        ScriptDefinition script,
        ScriptVersion version,
        IEnumerable<(ScriptParameterDefinition Definition, string Value)>? parameters = null)
    {
        var job = SubmittedJob(script, version, parameters);
        job.MarkValidated(Approver, job.UpdatedUtc.AddMinutes(1));
        job.QueueDryRun(Approver, job.UpdatedUtc.AddMinutes(1));
        job.StartDryRun(Approver, job.UpdatedUtc.AddMinutes(1));
        job.CompleteDryRun(Approver, job.UpdatedUtc.AddMinutes(1));
        job.RequireApproval(Approver, job.UpdatedUtc.AddMinutes(1));
        job.RecordApproval(
            Approver,
            Fingerprint,
            "Approved",
            job.UpdatedUtc.AddMinutes(1));
        job.QueueExecution(Approver, job.UpdatedUtc.AddMinutes(1));
        job.Claim(Approver, job.UpdatedUtc.AddMinutes(1));
        job.StartExecutionAttempt(null, Approver, job.UpdatedUtc.AddMinutes(1));
        job.BeginPostValidation(Approver, job.UpdatedUtc.AddMinutes(1));
        job.RecordTerminalExecutionOutcome(
            ExecutionOutcome.Succeeded,
            0,
            "Completed",
            Approver,
            job.UpdatedUtc.AddMinutes(1));
        return job;
    }

    public static WorkerNode Worker()
    {
        var worker = new WorkerNode(WorkerNodeId.New(), "worker-01", Time);
        worker.RegisterCapability(new WorkerCapability("PowerShell", "7.4"));
        worker.RecordHeartbeat(Time.AddMinutes(1));
        return worker;
    }

    public static CredentialReference Credential() =>
        new(
            CredentialReferenceId.New(),
            "externalvault",
            "externalvault://vault/automation/windows",
            "Windows automation",
            Time,
            Requester);
}
