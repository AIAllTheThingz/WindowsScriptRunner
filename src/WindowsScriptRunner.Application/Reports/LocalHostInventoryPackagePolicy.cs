using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Application.Reports;

internal static class LocalHostInventoryPackagePolicy
{
    internal const string ScriptPath =
        "windows.local-host-inventory/1.0.0/Collect-LocalHostInventory.ps1";
    internal const string ScriptSha256 =
        "b85b29bbfc04dfb9c85f3fcc391e58c1ea0ef8aeeddcb5b796d8968b3729c368";
    internal const string MinimumPowerShellVersion = "7.4.0";
    internal static readonly ScriptDefinitionId DefinitionId =
        new(Guid.Parse("7fc1cf27-4d30-48b2-9ae5-6b41a7f57758"));
    internal static readonly ScriptVersionId VersionId =
        new(Guid.Parse("6f1e7581-b7e2-4114-aa0f-28f90c95e6af"));
    private static readonly ScriptVersionNumber Version =
        ScriptVersionNumber.Parse(JobReport.LocalHostInventoryPackageVersion);

    internal static ScriptVersion Validate(ScriptDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var version = definition.Versions.SingleOrDefault(candidate => candidate.Id == VersionId)
            ?? throw new ApplicationConflictException(
                "The pinned script is not the reviewed Local Host Inventory package.");
        var valid =
            definition.Id == DefinitionId &&
            definition.IsEnabled &&
            string.Equals(
                definition.Name.Value,
                JobReport.LocalHostInventoryPackageId,
                StringComparison.Ordinal) &&
            definition.RiskLevel == RiskLevel.ReadOnly &&
            version.IsPublished &&
            version.Version == Version &&
            string.Equals(version.RelativeScriptPath, ScriptPath, StringComparison.Ordinal) &&
            string.Equals(version.Sha256, ScriptSha256, StringComparison.Ordinal) &&
            string.Equals(
                version.MinimumPowerShellVersion,
                MinimumPowerShellVersion,
                StringComparison.Ordinal) &&
            version.DefaultTimeoutMinutes == 1 &&
            version.ParameterDefinitions.Count == 0 &&
            version.SupportedPhases.Count == 1 &&
            version.SupportedPhases.Contains(ExecutionPhase.DryRun) &&
            version.SupportedReportFormats.Count == 1 &&
            version.SupportedReportFormats.Contains(ReportFormat.Json);
        if (!valid)
        {
            throw new ApplicationConflictException(
                "The pinned script is not the reviewed Local Host Inventory package.");
        }

        return version;
    }
}
