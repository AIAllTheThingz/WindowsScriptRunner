using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.PowerShell;

internal sealed record PowerShellCandidateSet(
    string? EnvironmentOverride,
    IReadOnlyList<string> PathCandidates,
    IReadOnlyList<string> StandardLocationCandidates);

internal interface IPowerShellCandidateSource
{
    PowerShellCandidateSet GetCandidates();
}

internal sealed class PowerShellCandidateSource : IPowerShellCandidateSource
{
    public PowerShellCandidateSet GetCandidates() =>
        CreateCandidates(
            Environment.GetEnvironmentVariable("WINDOWSSCRIPTRUNNER_PWSH_PATH"),
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("ProgramFiles"));

    internal static PowerShellCandidateSet CreateCandidates(
        string? environmentOverride,
        string? path,
        string? programFiles)
    {
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return new PowerShellCandidateSet(environmentOverride, [], []);
        }

        var pathCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var entry in path.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                if (Path.IsPathFullyQualified(entry))
                {
                    pathCandidates.Add(Path.Combine(entry, "pwsh.exe"));
                }
            }
        }

        var standardCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            standardCandidates.AddRange(EnumerateStandardCandidates(programFiles));
        }

        return new PowerShellCandidateSet(
            null,
            pathCandidates,
            standardCandidates);
    }

    internal static IReadOnlyList<string> EnumerateStandardCandidates(
        string programFiles,
        Func<string, IEnumerable<string>>? enumerateDirectories = null)
    {
        var powerShellRoot = Path.Combine(programFiles, "PowerShell");
        if (!Directory.Exists(powerShellRoot))
        {
            return [];
        }

        try
        {
            return (enumerateDirectories ?? Directory.EnumerateDirectories)(powerShellRoot)
                .Select(directory => Path.Combine(directory, "pwsh.exe"))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

internal sealed class PowerShellExecutableLocator(
    IOptions<PowerShellExecutionOptions> options,
    IPowerShellRuntimeProbe runtimeProbe,
    IPowerShellCandidateSource candidateSource,
    ILogger<PowerShellExecutableLocator> logger) : IPowerShellExecutableLocator
{
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);
    private readonly PowerShellExecutionOptions _options = options.Value;
    private PowerShellRuntimeInfo? _cachedRuntime;

    public async Task<PowerShellRuntimeInfo> LocateAsync(
        CancellationToken cancellationToken)
    {
        var cachedRuntime = Volatile.Read(ref _cachedRuntime);
        if (cachedRuntime is not null)
        {
            return cachedRuntime;
        }

        await _discoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedRuntime = Volatile.Read(ref _cachedRuntime);
            if (cachedRuntime is not null)
            {
                return cachedRuntime;
            }

            if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
            {
                var runtime = await ProbeAuthoritativeAsync(
                        _options.ExecutablePath,
                        cancellationToken)
                    .ConfigureAwait(false);
                Volatile.Write(ref _cachedRuntime, runtime);
                return runtime;
            }

            var candidates = candidateSource.GetCandidates();
            if (!string.IsNullOrWhiteSpace(candidates.EnvironmentOverride))
            {
                var runtime = await ProbeAuthoritativeAsync(
                        candidates.EnvironmentOverride,
                        cancellationToken)
                    .ConfigureAwait(false);
                Volatile.Write(ref _cachedRuntime, runtime);
                return runtime;
            }

            var uniqueCandidates = candidates.PathCandidates
                .Concat(candidates.StandardLocationCandidates)
                .Where(IsValidCandidatePath)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var compatible = new List<PowerShellRuntimeInfo>();
            foreach (var candidate in uniqueCandidates)
            {
                try
                {
                    compatible.Add(
                        await runtimeProbe.ProbeAsync(candidate, cancellationToken)
                            .ConfigureAwait(false));
                }
                catch (PowerShellRuntimeValidationException)
                {
                    logger.LogDebug("Rejected incompatible PowerShell runtime candidate.");
                }
            }

            var selectedRuntime = compatible
                .OrderBy(runtime => runtime.IsPreview)
                .ThenByDescending(runtime => runtime.Version)
                .ThenBy(runtime => runtime.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (selectedRuntime is null)
            {
                throw new PowerShellRuntimeNotFoundException(
                    "No compatible PowerShell 7 runtime was found.");
            }

            Volatile.Write(ref _cachedRuntime, selectedRuntime);
            logger.LogInformation(
                "Selected PowerShell {PowerShellVersion}.",
                selectedRuntime.Version);
            return selectedRuntime;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    private async Task<PowerShellRuntimeInfo> ProbeAuthoritativeAsync(
        string candidate,
        CancellationToken cancellationToken)
    {
        if (!IsValidCandidatePath(candidate))
        {
            throw new PowerShellRuntimeNotFoundException(
                "The configured PowerShell executable is invalid or missing.");
        }

        var runtime = await runtimeProbe
            .ProbeAsync(Path.GetFullPath(candidate), cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Selected PowerShell {PowerShellVersion}.",
            runtime.Version);
        return runtime;
    }

    private static bool IsValidCandidatePath(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        Path.IsPathFullyQualified(candidate) &&
        string.Equals(
            Path.GetFileName(candidate),
            "pwsh.exe",
            StringComparison.OrdinalIgnoreCase) &&
        File.Exists(candidate) &&
        !Directory.Exists(candidate);
}
