using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Automation;

internal sealed class LocalHostInventoryPackageRegistrar(
    IScriptDefinitionRepository scriptRepository,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IWorkerCoordinationClock coordinationClock)
{
    internal async Task<bool> RegisterAsync(CancellationToken cancellationToken)
    {
        var existing = await scriptRepository.GetByIdAsync(
            LocalHostInventoryPackageMetadata.DefinitionId,
            cancellationToken);
        if (existing is not null)
        {
            var version = existing.Versions.SingleOrDefault(candidate =>
                candidate.Id == LocalHostInventoryPackageMetadata.VersionId)
                ?? throw new AutomationPackageTrustException(
                    "The registered package does not contain the reviewed version.");
            LocalHostInventoryArtifactCatalog.ValidatePackage(existing, version);
            return false;
        }

        var now = await coordinationClock.GetUtcNowAsync(cancellationToken);
        var definition = LocalHostInventoryPackageMetadata.CreateDefinition(now);
        await scriptRepository.AddAsync(definition, cancellationToken);
        await auditWriter.WriteAsync(
            new AuditEvent(
                AuditEventId.New(),
                "AutomationPackageRegistered",
                nameof(ScriptDefinition),
                definition.Id.ToString(),
                new UserIdentity(LocalHostInventoryPackageMetadata.RegistrationActor),
                now,
                "The reviewed production automation package was registered.",
                new Dictionary<string, string>
                {
                    ["PackageId"] = LocalHostInventoryPackageMetadata.PackageId,
                    ["PackageVersion"] = LocalHostInventoryPackageMetadata.PackageVersion,
                    ["RiskLevel"] = RiskLevel.ReadOnly.ToString(),
                }),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return true;
    }
}
