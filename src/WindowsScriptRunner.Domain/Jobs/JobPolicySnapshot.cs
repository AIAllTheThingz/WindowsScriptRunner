using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Scripts;

namespace WindowsScriptRunner.Domain.Jobs;

public sealed record JobPolicySnapshot
{
    private JobPolicySnapshot(
        ScriptDefinitionId scriptDefinitionId,
        ScriptVersionId scriptVersionId,
        RiskLevel riskLevel,
        bool supportsExecutePhase)
    {
        ScriptDefinitionId = scriptDefinitionId;
        ScriptVersionId = scriptVersionId;
        RiskLevel = riskLevel;
        SupportsExecutePhase = supportsExecutePhase;
    }

    public ScriptDefinitionId ScriptDefinitionId { get; }
    public ScriptVersionId ScriptVersionId { get; }
    public RiskLevel RiskLevel { get; }
    public bool SupportsExecutePhase { get; }

    internal static JobPolicySnapshot Capture(
        ScriptDefinition definition,
        ScriptVersionId expectedVersionId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var version = definition.GetVersion(expectedVersionId);
        if (!version.IsPublished)
        {
            throw new DomainValidationException("Only a published script version can be submitted.");
        }

        return new JobPolicySnapshot(
            definition.Id,
            version.Id,
            definition.RiskLevel,
            version.SupportedPhases.Contains(ExecutionPhase.Execute));
    }
}
