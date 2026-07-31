using System.Globalization;
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

        EnsureCompatiblePowerShellConfiguration(configuration);
        services.AddPowerShellExecutionBoundary(configuration);
        services.AddSingleton<LocalHostInventoryArtifactCatalog>();
        services.AddSingleton<LocalHostInventoryReportParser>();
        services.AddSingleton<LocalHostInventoryPackageRegistrar>();
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

    private static void EnsureCompatiblePowerShellConfiguration(
        IConfiguration configuration)
    {
        var defaults = new PowerShellExecutionOptions();
        var configuredValue = configuration[
            $"{PowerShellExecutionOptions.SectionName}:{nameof(PowerShellExecutionOptions.MinimumVersion)}"]
            ?? defaults.MinimumVersion;
        if (!Version.TryParse(configuredValue, out var configuredMinimum) ||
            !Version.TryParse(
                LocalHostInventoryPackageMetadata.MinimumPowerShellVersion,
                out var packageMinimum) ||
            configuredMinimum < packageMinimum)
        {
            throw new InvalidOperationException(
                "The enabled automation package requires a compatible minimum PowerShell version.");
        }

        var maximumTimeoutValue = configuration[
            $"{PowerShellExecutionOptions.SectionName}:{nameof(PowerShellExecutionOptions.MaximumTimeoutSeconds)}"]
            ?? defaults.MaximumTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        if (!int.TryParse(
                maximumTimeoutValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maximumTimeoutSeconds) ||
            maximumTimeoutSeconds <
                LocalHostInventoryPackageMetadata.DefaultTimeoutMinutes * 60)
        {
            throw new InvalidOperationException(
                "The enabled automation package requires a compatible maximum execution timeout.");
        }
    }
}
