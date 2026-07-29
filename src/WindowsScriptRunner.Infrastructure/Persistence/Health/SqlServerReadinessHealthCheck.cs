using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace WindowsScriptRunner.Infrastructure.Persistence.Health;

public sealed class SqlServerReadinessHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<SqlServerReadinessHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<WindowsScriptRunnerDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("SQL Server is unavailable.");
            }

            var pendingMigrations = await dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken);
            return pendingMigrations.Any()
                ? HealthCheckResult.Unhealthy("Required database migrations are pending.")
                : HealthCheckResult.Healthy("SQL Server is ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "SQL Server readiness failed with category {Category}",
                exception.GetType().Name);
            return HealthCheckResult.Unhealthy("SQL Server readiness validation failed.");
        }
    }
}
