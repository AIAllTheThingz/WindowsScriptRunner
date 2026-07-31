using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Exceptions;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.Automation;

internal sealed class LocalHostInventoryJobWorkHandler(
    IServiceScopeFactory scopeFactory,
    LocalHostInventoryArtifactCatalog catalog,
    IPowerShellExecutionBoundary executionBoundary) : IJobWorkHandler
{
    public IReadOnlySet<JobWorkRoute> SupportedRoutes =>
        LocalHostInventoryPackageMetadata.SupportedRoutes;

    public async Task HandleAsync(
        ClaimedJobWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            var request = await PrepareAsync(work, cancellationToken);
            await StartDryRunAsync(work, cancellationToken);
            var result = await executionBoundary.ExecuteAsync(
                request,
                cancellationToken);
            var mapping = LocalHostInventoryResultMapper.Map(result);
            if (mapping.Succeeded)
            {
                await CompleteAsync(work, cancellationToken);
                return;
            }

            await TerminateAsync(
                work,
                mapping.Outcome ??
                    throw new AutomationPackageTrustException(
                        "A failed package result requires a terminal outcome."),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!await IsCurrentLeaseAsync(work))
            {
                throw;
            }

            await TerminateAsync(
                work,
                ExecutionOutcome.Cancelled,
                CancellationToken.None);
        }
        catch (AutomationPackageTrustException)
        {
            await TerminateAsync(work, ExecutionOutcome.Blocked, CancellationToken.None);
        }
        catch (PowerShellScriptTrustException)
        {
            await TerminateAsync(work, ExecutionOutcome.Blocked, CancellationToken.None);
        }
        catch (PowerShellRuntimeNotFoundException)
        {
            await TerminateAsync(work, ExecutionOutcome.NotRun, CancellationToken.None);
        }
        catch (PowerShellRuntimeValidationException)
        {
            await TerminateAsync(work, ExecutionOutcome.NotRun, CancellationToken.None);
        }
        catch (PowerShellProcessStartException)
        {
            await TerminateAsync(work, ExecutionOutcome.NotRun, CancellationToken.None);
        }
        catch (PowerShellExecutionException)
        {
            await TerminateAsync(work, ExecutionOutcome.Failed, CancellationToken.None);
        }
        catch (DomainException)
        {
            await TerminateAsync(work, ExecutionOutcome.Blocked, CancellationToken.None);
        }
    }

    private async Task<PowerShellExecutionRequest> PrepareAsync(
        ClaimedJobWork work,
        CancellationToken cancellationToken)
    {
        EnsureSupportedRoute(work);
        await using var scope = scopeFactory.CreateAsyncScope();
        var inspection = await scope.ServiceProvider
            .GetRequiredService<InspectJobLeaseHandler>()
            .HandleAsync(
                new InspectJobLeaseQuery(work.JobId, work.Credentials),
                cancellationToken);
        if (!inspection.IsCurrent)
        {
            throw new ApplicationConflictException(
                "The claimed package lease is no longer current.");
        }

        var job = await scope.ServiceProvider
            .GetRequiredService<IJobRepository>()
            .GetByIdAsync(work.JobId, cancellationToken)
            ?? throw new AutomationPackageTrustException(
                "The claimed package job could not be loaded.");
        var now = await scope.ServiceProvider
            .GetRequiredService<IWorkerCoordinationClock>()
            .GetUtcNowAsync(cancellationToken);
        job.ValidateWorkLease(work.Credentials, JobWorkKind.DryRun, now);
        if (job.ScriptVersionId != work.ScriptVersionId)
        {
            throw new AutomationPackageTrustException(
                "The claimed job script version does not match the supported route.");
        }

        var definition = await scope.ServiceProvider
            .GetRequiredService<IScriptDefinitionRepository>()
            .GetByIdAsync(job.ScriptDefinitionId, cancellationToken)
            ?? throw new AutomationPackageTrustException(
                "The pinned package definition could not be loaded.");
        var version = definition.Versions.SingleOrDefault(candidate =>
            candidate.Id == job.ScriptVersionId)
            ?? throw new AutomationPackageTrustException(
                "The pinned package version could not be loaded.");
        var trustedScript = catalog.Resolve(definition, version);
        var arguments = LocalHostInventoryParameterMapper.Map(
            job,
            version,
            LocalHostInventoryPackageMetadata.AllowedParameterNames);
        return new PowerShellExecutionRequest(
            new PowerShellExecutionId(job.Id.Value),
            trustedScript,
            arguments,
            TimeSpan.FromMinutes(version.DefaultTimeoutMinutes));
    }

    private async Task StartDryRunAsync(
        ClaimedJobWork work,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<StartLeasedDryRunHandler>()
            .HandleAsync(
                new StartLeasedDryRunCommand(
                    work.JobId,
                    work.Credentials,
                    Actor(work)),
                cancellationToken);
    }

    private async Task CompleteAsync(
        ClaimedJobWork work,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<CompleteLeasedReadOnlyDryRunHandler>()
            .HandleAsync(
                new CompleteLeasedReadOnlyDryRunCommand(
                    work.JobId,
                    work.Credentials,
                    Actor(work)),
                cancellationToken);
    }

    private async Task TerminateAsync(
        ClaimedJobWork work,
        ExecutionOutcome outcome,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<TerminateLeasedDryRunHandler>()
            .HandleAsync(
                new TerminateLeasedDryRunCommand(
                    work.JobId,
                    work.Credentials,
                    outcome,
                    Actor(work)),
                cancellationToken);
    }

    private async Task<bool> IsCurrentLeaseAsync(ClaimedJobWork work)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var inspection = await scope.ServiceProvider
            .GetRequiredService<InspectJobLeaseHandler>()
            .HandleAsync(
                new InspectJobLeaseQuery(work.JobId, work.Credentials),
                CancellationToken.None);
        return inspection.IsCurrent;
    }

    private static void EnsureSupportedRoute(ClaimedJobWork work)
    {
        var route = new JobWorkRoute(work.WorkKind, work.ScriptVersionId);
        if (!LocalHostInventoryPackageMetadata.SupportedRoutes.Contains(route))
        {
            throw new AutomationPackageTrustException(
                "The claimed work route is not supported by this package.");
        }
    }

    private static UserIdentity Actor(ClaimedJobWork work) =>
        new($"worker:{work.WorkerNodeId}");
}
