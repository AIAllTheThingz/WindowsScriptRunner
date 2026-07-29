using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application;
using WindowsScriptRunner.Application.Abstractions;

namespace WindowsScriptRunner.IntegrationTests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void ApplicationRegistrationBuildsWithoutExternalInfrastructure()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IClock>());
    }

    [Fact]
    public void DomainApplicationAndContractsAssembliesLoadTogether()
    {
        Assert.Equal(
            "WindowsScriptRunner.Domain",
            typeof(Domain.AssemblyMarker).Assembly.GetName().Name);
        Assert.Equal(
            "WindowsScriptRunner.Application",
            typeof(Application.AssemblyMarker).Assembly.GetName().Name);
        Assert.Equal(
            "WindowsScriptRunner.Contracts",
            typeof(Contracts.AssemblyMarker).Assembly.GetName().Name);
    }
}
