using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

public sealed class PowerShellContractAndOptionsTests
{
    [Fact]
    public void ExecutionIdentifierIsStrongAndRejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new PowerShellExecutionId(Guid.Empty));
        var first = PowerShellExecutionId.New();
        var copy = new PowerShellExecutionId(first.Value);

        Assert.Equal(first, copy);
        Assert.NotEqual(first, PowerShellExecutionId.New());
        Assert.Equal(first.Value.ToString("D"), first.ToString());
    }

    [Fact]
    public void TrustedScriptCannotBeConstructedByProductionCallers()
    {
        Assert.Empty(typeof(TrustedPowerShellScript).GetConstructors());
        Assert.NotEmpty(
            typeof(TrustedPowerShellScript).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void PublicExecutionMethodAcceptsNoRawPathOrCommandText()
    {
        var method = Assert.Single(typeof(IPowerShellExecutionBoundary).GetMethods());
        var parameters = method.GetParameters();

        Assert.Equal(typeof(PowerShellExecutionRequest), parameters[0].ParameterType);
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.ParameterType == typeof(string));
    }

    [Fact]
    public void RecommendedOptionsDefaultsAreStable()
    {
        var options = new PowerShellExecutionOptions();

        Assert.Equal("7.4.0", options.MinimumVersion);
        Assert.Equal(10, options.ProbeTimeoutSeconds);
        Assert.Equal(300, options.DefaultTimeoutSeconds);
        Assert.Equal(3600, options.MaximumTimeoutSeconds);
        Assert.Equal(10, options.TerminationGraceSeconds);
        Assert.Equal(1_048_576, options.MaximumStandardOutputBytes);
        Assert.Equal(1_048_576, options.MaximumStandardErrorBytes);
        Assert.Equal(2_097_152, options.MaximumCombinedOutputBytes);
        Assert.True(options.Require64Bit);
        Assert.False(options.AllowPreviewVersion);
    }

    [Fact]
    public void CompleteAbsoluteOptionsAreAccepted()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var options = new PowerShellExecutionOptions
        {
            AllowedScriptRoot = Path.Combine(root, "wsr-allowed"),
            WorkingRoot = Path.Combine(root, "wsr-working"),
        };

        Assert.True(new PowerShellExecutionOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void ExplicitRegistrationBuildsBoundaryWithoutWebOrWorkerComposition()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WindowsScriptRunner.RegistrationTests");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{PowerShellExecutionOptions.SectionName}:AllowedScriptRoot"] =
                        Path.Combine(root, "allowed"),
                    [$"{PowerShellExecutionOptions.SectionName}:WorkingRoot"] =
                        Path.Combine(root, "working"),
                })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddPowerShellExecutionBoundary(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<PowerShellExecutionBoundary>(
            provider.GetRequiredService<IPowerShellExecutionBoundary>());
        Assert.IsType<PowerShellExecutableLocator>(
            provider.GetRequiredService<IPowerShellExecutableLocator>());
    }

    [Fact]
    public void RegisteredInvalidOptionsFailWhenResolved()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPowerShellExecutionBoundary(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IPowerShellExecutionBoundary>());
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void UnsafeOptionsAreRejected(Action<PowerShellExecutionOptions> change)
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var options = new PowerShellExecutionOptions
        {
            AllowedScriptRoot = Path.Combine(root, "wsr-allowed"),
            WorkingRoot = Path.Combine(root, "wsr-working"),
        };
        change(options);

        Assert.False(new PowerShellExecutionOptionsValidator().Validate(null, options).Succeeded);
    }

    public static TheoryData<Action<PowerShellExecutionOptions>> InvalidOptions => new()
    {
        options => options.ExecutablePath = "pwsh.exe",
        options => options.ExecutablePath = Path.Combine(
            Path.GetPathRoot(Environment.SystemDirectory)!,
            "powershell.exe"),
        options => options.MinimumVersion = "preview",
        options => options.ProbeTimeoutSeconds = 0,
        options => options.DefaultTimeoutSeconds = 0,
        options => options.MaximumTimeoutSeconds = 0,
        options =>
        {
            options.DefaultTimeoutSeconds = 20;
            options.MaximumTimeoutSeconds = 10;
        },
        options => options.TerminationGraceSeconds = 0,
        options => options.MaximumStandardOutputBytes = 0,
        options => options.MaximumStandardErrorBytes = 0,
        options => options.MaximumCombinedOutputBytes = 0,
        options =>
        {
            options.MaximumStandardOutputBytes = 10;
            options.MaximumCombinedOutputBytes = 5;
        },
        options => options.AllowedScriptRoot = "relative",
        options => options.WorkingRoot = "relative",
        options => options.WorkingRoot = options.AllowedScriptRoot,
        options => options.AllowedScriptRoot = @"\\server\share",
        options => options.WorkingRoot = @"\\?\C:\work",
        options => options.WorkingRoot += ":alternate",
    };
}
