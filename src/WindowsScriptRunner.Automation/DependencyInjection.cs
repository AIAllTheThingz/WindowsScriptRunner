using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.PowerShell;
using WindowsScriptRunner.Reporting;

namespace WindowsScriptRunner.Automation;

public static class DependencyInjection
{
    public static IServiceCollection AddProductionAutomation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(
            LocalHostInventoryPackageOptions.SectionName);
        services.AddOptions<LocalHostInventoryPackageOptions>()
            .Bind(section)
            .Validate(
                options => !options.RegisterOnStartup || options.Enabled,
                "Package registration requires package enablement.")
            .ValidateOnStart();

        var enabled = ReadBoolean(section, nameof(LocalHostInventoryPackageOptions.Enabled));
        var registerOnStartup = ReadBoolean(
            section,
            nameof(LocalHostInventoryPackageOptions.RegisterOnStartup));
        if (registerOnStartup && !enabled)
        {
            throw new InvalidOperationException(
                "Package registration requires package enablement.");
        }

        if (!enabled)
        {
            return services;
        }

        EnsureCompatibleMinimumPowerShellVersion(configuration);
        services.AddPowerShellExecutionBoundary(configuration);
        services.AddSingleton<LocalHostInventoryArtifactCatalog>();
        services.AddSingleton<LocalHostInventoryReportParser>();
        services.AddTransient<LocalHostInventoryPackageRegistrar>();
        services.AddSingleton<IJobWorkHandler, LocalHostInventoryJobWorkHandler>();
        services.AddHostedService<LocalHostInventoryPackageStartupService>();
        return services;
    }

    private static bool ReadBoolean(IConfigurationSection section, string key)
    {
        var value = section[key];
        if (value is null)
        {
            return false;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Configuration value '{section.Path}:{key}' must be true or false.");
    }

    private static void EnsureCompatibleMinimumPowerShellVersion(
        IConfiguration configuration)
    {
        var configuredValue = configuration[
            $"{PowerShellExecutionOptions.SectionName}:{nameof(PowerShellExecutionOptions.MinimumVersion)}"]
            ?? new PowerShellExecutionOptions().MinimumVersion;
        if (!Version.TryParse(configuredValue, out var configuredMinimum) ||
            !Version.TryParse(
                LocalHostInventoryPackageMetadata.MinimumPowerShellVersion,
                out var packageMinimum) ||
            configuredMinimum < packageMinimum)
        {
            throw new InvalidOperationException(
                "The enabled automation package requires a compatible minimum PowerShell version.");
        }
    }
}
