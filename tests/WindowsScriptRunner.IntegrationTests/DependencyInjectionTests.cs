using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application;

namespace WindowsScriptRunner.IntegrationTests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void ApplicationRegistrationBuildsWithoutExternalInfrastructure()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }
}
