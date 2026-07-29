using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Infrastructure.Persistence;
using WindowsScriptRunner.Infrastructure.Persistence.Health;
using WindowsScriptRunner.Infrastructure.Persistence.Repositories;

namespace WindowsScriptRunner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SqlServerPersistenceOptions>()
            .Bind(configuration.GetSection(SqlServerPersistenceOptions.SectionName))
            .Validate(options => options.IsValid(), "Persistence options are outside supported bounds.")
            .ValidateOnStart();
        services.AddDbContext<WindowsScriptRunnerDbContext>(
            (serviceProvider, options) =>
            {
                var persistence = serviceProvider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<SqlServerPersistenceOptions>>()
                    .Value;
                var connectionString =
                    configuration.GetConnectionString("WindowsScriptRunner") ?? string.Empty;
                options.UseSqlServer(
                    connectionString,
                    sql =>
                    {
                        sql.MigrationsHistoryTable("__EFMigrationsHistory", "wsr");
                        sql.EnableRetryOnFailure(
                            persistence.RetryCount,
                            TimeSpan.FromSeconds(persistence.RetryDelaySeconds),
                            null);
                        sql.CommandTimeout(persistence.CommandTimeoutSeconds);
                    });
                options.EnableDetailedErrors(persistence.EnableDetailedErrors);
                options.EnableSensitiveDataLogging(false);
            });
        services.AddScoped<IJobRepository, SqlJobRepository>();
        services.AddScoped<IScriptDefinitionRepository, SqlScriptDefinitionRepository>();
        services.AddScoped<IWorkerNodeRepository, SqlWorkerNodeRepository>();
        services.AddScoped<ICredentialReferenceRepository, SqlCredentialReferenceRepository>();
        services.AddScoped<IAuditWriter, SqlAuditWriter>();
        services.AddScoped<IUnitOfWork, SqlUnitOfWork>();
        services.AddHostedService<SqlServerMigrationHostedService>();
        services.AddHealthChecks()
            .AddCheck(
                "self",
                () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
                tags: ["live"])
            .AddCheck<SqlServerReadinessHealthCheck>("sql-server", tags: ["ready"]);

        return services;
    }
}
