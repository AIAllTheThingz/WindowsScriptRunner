using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.Automation;

internal sealed class LocalHostInventoryArtifactCatalog(
    IReviewedPowerShellArtifactFactory artifactFactory)
{
    internal TrustedPowerShellScript Resolve(
        ScriptDefinition definition,
        ScriptVersion version)
    {
        ValidatePackage(definition, version);
        return artifactFactory.Resolve(LocalHostInventoryPackageMetadata.Artifact);
    }

    internal void ValidateArtifact() =>
        _ = artifactFactory.Resolve(LocalHostInventoryPackageMetadata.Artifact);

    internal static void ValidatePackage(
        ScriptDefinition definition,
        ScriptVersion version)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(version);
        var metadataMatches =
            definition.Id == LocalHostInventoryPackageMetadata.DefinitionId &&
            definition.Name.Value == LocalHostInventoryPackageMetadata.PackageId &&
            definition.DisplayName == LocalHostInventoryPackageMetadata.DisplayName &&
            definition.Description == LocalHostInventoryPackageMetadata.Description &&
            definition.RiskLevel == RiskLevel.ReadOnly &&
            definition.IsEnabled &&
            definition.CreatedBy.Value == LocalHostInventoryPackageMetadata.RegistrationActor &&
            definition.Versions.Count == 1 &&
            version.Id == LocalHostInventoryPackageMetadata.VersionId &&
            version.Version == ScriptVersionNumber.Parse(
                LocalHostInventoryPackageMetadata.PackageVersion) &&
            version.RelativeScriptPath == LocalHostInventoryPackageMetadata.RelativeScriptPath &&
            version.Sha256 == LocalHostInventoryPackageMetadata.Sha256 &&
            version.GitCommitSha is null &&
            version.MinimumPowerShellVersion ==
                LocalHostInventoryPackageMetadata.MinimumPowerShellVersion &&
            version.DefaultTimeoutMinutes ==
                LocalHostInventoryPackageMetadata.DefaultTimeoutMinutes &&
            version.CreatedBy.Value == LocalHostInventoryPackageMetadata.RegistrationActor &&
            version.IsPublished &&
            version.SupportedPhases.Count == 1 &&
            version.SupportedPhases.Contains(ExecutionPhase.DryRun) &&
            version.SupportedReportFormats.Count == 1 &&
            version.SupportedReportFormats.Contains(ReportFormat.Json) &&
            version.ParameterDefinitions.Count == 0;
        if (!metadataMatches)
        {
            throw new AutomationPackageTrustException(
                "The pinned package metadata does not match the reviewed catalog.");
        }
    }
}
