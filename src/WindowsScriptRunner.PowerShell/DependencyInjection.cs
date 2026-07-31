using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.PowerShell;

public static class DependencyInjection
{
    public static IServiceCollection AddPowerShellExecutionBoundary(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PowerShellExecutionOptions>()
            .Bind(configuration.GetSection(PowerShellExecutionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<PowerShellExecutionOptions>,
            PowerShellExecutionOptionsValidator>();
        services.AddSingleton<IPowerShellCandidateSource, PowerShellCandidateSource>();
        services.AddSingleton<IProcessTreeController, ProcessTreeController>();
        services.AddSingleton<IPowerShellRuntimeProbe, PowerShellRuntimeProbe>();
        services.AddSingleton<IPowerShellExecutableLocator, PowerShellExecutableLocator>();
        services.AddSingleton<
            IPowerShellScriptTrustValidator,
            PowerShellScriptTrustValidator>();
        services.AddSingleton<
            IReviewedPowerShellArtifactFactory,
            ReviewedPowerShellArtifactFactory>();
        services.AddSingleton<IPowerShellArgumentValidator, PowerShellArgumentValidator>();
        services.AddSingleton<IExecutionWorkingDirectory, ExecutionWorkingDirectory>();
        services.AddSingleton<IPowerShellExecutionBoundary, PowerShellExecutionBoundary>();
        return services;
    }
}
