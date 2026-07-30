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
    public PowerShellCandidateSet GetCandidates()
    {
        var pathCandidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
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
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var powerShellRoot = Path.Combine(programFiles, "PowerShell");
            if (Directory.Exists(powerShellRoot))
            {
                standardCandidates.AddRange(
                    Directory.EnumerateDirectories(powerShellRoot)
                        .Select(directory => Path.Combine(directory, "pwsh.exe")));
            }
        }

        return new PowerShellCandidateSet(
            Environment.GetEnvironmentVariable("WINDOWSSCRIPTRUNNER_PWSH_PATH"),
            pathCandidates,
            standardCandidates);
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
        if (_cachedRuntime is not null)
        {
            return _cachedRuntime;
        }

        await _discoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedRuntime is not null)
            {
                return _cachedRuntime;
            }

            if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
            {
                _cachedRuntime = await ProbeAuthoritativeAsync(
                        _options.ExecutablePath,
                        cancellationToken)
                    .ConfigureAwait(false);
                return _cachedRuntime;
            }

            var candidates = candidateSource.GetCandidates();
            if (!string.IsNullOrWhiteSpace(candidates.EnvironmentOverride))
            {
                _cachedRuntime = await ProbeAuthoritativeAsync(
                        candidates.EnvironmentOverride,
                        cancellationToken)
                    .ConfigureAwait(false);
                return _cachedRuntime;
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
                catch (PowerShellExecutionException)
                {
                    logger.LogDebug(
                        "Rejected PowerShell runtime candidate {ExecutablePath}.",
                        candidate);
                }
            }

            _cachedRuntime = compatible
                .OrderBy(runtime => runtime.IsPreview)
                .ThenByDescending(runtime => runtime.Version)
                .ThenBy(runtime => runtime.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (_cachedRuntime is null)
            {
                throw new PowerShellRuntimeNotFoundException(
                    "No compatible PowerShell 7 runtime was found.");
            }

            logger.LogInformation(
                "Selected PowerShell {PowerShellVersion} at {ExecutablePath}.",
                _cachedRuntime.Version,
                _cachedRuntime.ExecutablePath);
            return _cachedRuntime;
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
            "Selected PowerShell {PowerShellVersion} at {ExecutablePath}.",
            runtime.Version,
            runtime.ExecutablePath);
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
