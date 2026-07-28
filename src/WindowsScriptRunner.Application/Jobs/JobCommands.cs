using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Application.Jobs;

public sealed record CreateDraftJobCommand(
    ScriptDefinitionId ScriptDefinitionId,
    ScriptVersionId ScriptVersionId,
    ExecutionPhase RequestedPhase,
    UserIdentity RequestedBy,
    string? Description = null,
    ChangeReference? ChangeReference = null);

public sealed record AddJobTargetCommand(
    JobId JobId,
    TargetName TargetName,
    UserIdentity ActingUser);

public sealed record SetJobParameterCommand(
    JobId JobId,
    string ParameterName,
    string? SerializedValue,
    UserIdentity ActingUser);

public sealed record SubmitJobCommand(JobId JobId, UserIdentity ActingUser);

public sealed record TransitionJobCommand(
    JobId JobId,
    JobStatus NewStatus,
    UserIdentity ActingUser);

public sealed record GetJobQuery(JobId JobId);
