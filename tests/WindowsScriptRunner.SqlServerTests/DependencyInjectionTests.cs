using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Infrastructure;
using WindowsScriptRunner.Infrastructure.Persistence;

namespace WindowsScriptRunner.SqlServerTests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task InfrastructureRegistrationsShareOneScopedDbContextAndKeepMigrationsDisabled()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:WindowsScriptRunner"] = database.ConnectionString,
                    ["Persistence:ApplyMigrationsOnStartup"] = "false",
                    ["Persistence:EnableDetailedErrors"] = "false",
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        await using var firstScope = provider.CreateAsyncScope();
        var firstContext = firstScope.ServiceProvider
            .GetRequiredService<WindowsScriptRunnerDbContext>();

        Assert.Same(
            firstContext,
            firstScope.ServiceProvider.GetRequiredService<WindowsScriptRunnerDbContext>());
        Assert.NotNull(firstScope.ServiceProvider.GetRequiredService<IJobRepository>());
        Assert.NotNull(firstScope.ServiceProvider.GetRequiredService<IScriptDefinitionRepository>());
        Assert.NotNull(firstScope.ServiceProvider.GetRequiredService<IWorkerNodeRepository>());
        Assert.NotNull(firstScope.ServiceProvider.GetRequiredService<ICredentialReferenceRepository>());
        Assert.NotNull(firstScope.ServiceProvider.GetRequiredService<IAuditWriter>());
        Assert.NotNull(firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.False(
            firstScope.ServiceProvider
                .GetRequiredService<IOptions<SqlServerPersistenceOptions>>()
                .Value
                .ApplyMigrationsOnStartup);

        await using var secondScope = provider.CreateAsyncScope();
        Assert.NotSame(
            firstContext,
            secondScope.ServiceProvider.GetRequiredService<WindowsScriptRunnerDbContext>());
    }
}
