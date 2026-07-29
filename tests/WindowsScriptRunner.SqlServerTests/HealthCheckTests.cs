using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsScriptRunner.Infrastructure.Persistence;
using WindowsScriptRunner.Infrastructure.Persistence.Health;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class HealthCheckTests
{
    [Fact]
    public async Task ReadinessIsHealthyForMigratedDatabaseAndUnhealthyAfterDatabaseRemoval()
    {
        var database = await SqlServerDatabase.CreateAsync();
        var services = new ServiceCollection();
        services.AddDbContext<WindowsScriptRunnerDbContext>(
            options => options.UseSqlServer(
                database.ConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "wsr")));
        await using var provider = services.BuildServiceProvider();
        var healthCheck = new SqlServerReadinessHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SqlServerReadinessHealthCheck>.Instance);

        var healthy = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        await using (var context = database.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("0");
        }

        var pending = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        await database.DisposeAsync();
        var unhealthy = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, healthy.Status);
        Assert.Equal(HealthStatus.Unhealthy, pending.Status);
        Assert.Equal("Required database migrations are pending.", pending.Description);
        Assert.Equal(HealthStatus.Unhealthy, unhealthy.Status);
        Assert.DoesNotContain(
            "Server=",
            unhealthy.Description,
            StringComparison.OrdinalIgnoreCase);
    }
}
