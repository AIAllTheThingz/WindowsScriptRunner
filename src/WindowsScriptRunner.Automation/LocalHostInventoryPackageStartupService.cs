using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.Automation;

internal sealed class LocalHostInventoryPackageStartupService(
    IServiceScopeFactory scopeFactory,
    LocalHostInventoryArtifactCatalog catalog,
    IOptions<LocalHostInventoryPackageOptions> options,
    Microsoft.Extensions.Logging.ILogger<LocalHostInventoryPackageStartupService> logger) :
    Microsoft.Extensions.Hosting.IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        catalog.ValidateArtifact();
        var registered = false;
        if (options.Value.RegisterOnStartup)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            registered = await scope.ServiceProvider
                .GetRequiredService<LocalHostInventoryPackageRegistrar>()
                .RegisterAsync(cancellationToken);
        }

        logger.LogInformation(
            "Automation package {PackageId} version {PackageVersion} is enabled. Registered on this startup: {Registered}.",
            LocalHostInventoryPackageMetadata.PackageId,
            LocalHostInventoryPackageMetadata.PackageVersion,
            registered);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
