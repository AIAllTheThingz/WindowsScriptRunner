using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Auditing;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Automation;

internal sealed class LocalHostInventoryPackageRegistrar(
    IServiceScopeFactory scopeFactory)
{
    internal async Task<bool> RegisterAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RegisterInFreshScopeAsync(cancellationToken);
        }
        catch (ApplicationConflictException)
        {
            return await RegisterInFreshScopeAsync(cancellationToken);
        }
    }

    private async Task<bool> RegisterInFreshScopeAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scriptRepository = scope.ServiceProvider
            .GetRequiredService<IScriptDefinitionRepository>();
        var auditWriter = scope.ServiceProvider
            .GetRequiredService<IAuditWriter>();
        var unitOfWork = scope.ServiceProvider
            .GetRequiredService<IUnitOfWork>();
        var coordinationClock = scope.ServiceProvider
            .GetRequiredService<IWorkerCoordinationClock>();
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
