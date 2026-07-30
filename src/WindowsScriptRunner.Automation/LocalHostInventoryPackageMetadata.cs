using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.Automation;

internal static class LocalHostInventoryPackageMetadata
{
    internal const string PackageId = "windows.local-host-inventory";
    internal const string PackageVersion = "1.0.0";
    internal const string DisplayName = "Windows Local Host Inventory";
    internal const string Description =
        "Collects a bounded inventory of the local Windows worker host.";
    internal const string RelativeScriptPath =
        "windows.local-host-inventory/1.0.0/Collect-LocalHostInventory.ps1";
    internal const string Sha256 =
        "b85b29bbfc04dfb9c85f3fcc391e58c1ea0ef8aeeddcb5b796d8968b3729c368";
    internal const string MinimumPowerShellVersion = "7.4.0";
    internal const int DefaultTimeoutMinutes = 1;
    internal const string RegistrationActor = "system:phase6-package-registration";

    internal static ScriptDefinitionId DefinitionId { get; } =
        new(Guid.Parse("7fc1cf27-4d30-48b2-9ae5-6b41a7f57758"));

    internal static ScriptVersionId VersionId { get; } =
        new(Guid.Parse("6f1e7581-b7e2-4114-aa0f-28f90c95e6af"));

    internal static IReadOnlySet<JobWorkRoute> SupportedRoutes { get; } =
        new HashSet<JobWorkRoute>
        {
            new(JobWorkKind.DryRun, VersionId),
        };

    internal static IReadOnlySet<string> AllowedParameterNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal static ReviewedPowerShellArtifact Artifact { get; } =
        new(
            PackageId,
            RelativeScriptPath,
            Sha256,
            AllowedParameterNames);

    internal static ScriptDefinition CreateDefinition(DateTimeOffset createdUtc)
    {
        var actor = new UserIdentity(RegistrationActor);
        var definition = ScriptDefinition.Create(
            DefinitionId,
            new ScriptName(PackageId),
            DisplayName,
            Description,
            RiskLevel.ReadOnly,
            actor,
            createdUtc);
        var version = new ScriptVersion(
            VersionId,
            ScriptVersionNumber.Parse(PackageVersion),
            RelativeScriptPath,
            Sha256,
            null,
            MinimumPowerShellVersion,
            DefaultTimeoutMinutes,
            [ExecutionPhase.DryRun],
            [ReportFormat.Json],
            createdUtc,
            actor);
        version.Publish();
        definition.AddVersion(version, createdUtc);
        return definition;
    }
}
