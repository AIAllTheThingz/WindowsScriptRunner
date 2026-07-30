using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.PowerShell;

public sealed class PowerShellExecutionOptions
{
    public const string SectionName = "PowerShellExecution";
    public const int MaximumArgumentCount = 16;
    public const int MaximumArgumentValueLength = 8192;

    public string? ExecutablePath { get; set; }

    public string? AllowedScriptRoot { get; set; }

    public string? WorkingRoot { get; set; }

    public string MinimumVersion { get; set; } = "7.4.0";

    public int ProbeTimeoutSeconds { get; set; } = 10;

    public int DefaultTimeoutSeconds { get; set; } = 300;

    public int MaximumTimeoutSeconds { get; set; } = 3600;

    public int TerminationGraceSeconds { get; set; } = 10;

    public int MaximumStandardOutputBytes { get; set; } = 1_048_576;

    public int MaximumStandardErrorBytes { get; set; } = 1_048_576;

    public int MaximumCombinedOutputBytes { get; set; } = 2_097_152;

    public bool Require64Bit { get; set; } = true;

    public bool AllowPreviewVersion { get; set; }
}

public sealed class PowerShellExecutionOptionsValidator :
    IValidateOptions<PowerShellExecutionOptions>
{
    public ValidateOptionsResult Validate(string? name, PowerShellExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.ExecutablePath is not null &&
            (!Path.IsPathFullyQualified(options.ExecutablePath) ||
             !string.Equals(
                 Path.GetFileName(options.ExecutablePath),
                 "pwsh.exe",
                 StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add("ExecutablePath must be a fully qualified pwsh.exe path.");
        }

        if (!Version.TryParse(options.MinimumVersion, out _))
        {
            failures.Add("MinimumVersion must be a valid stable version.");
        }

        RequireRange(
            options.ProbeTimeoutSeconds,
            1,
            60,
            nameof(options.ProbeTimeoutSeconds),
            failures);
        RequireRange(
            options.DefaultTimeoutSeconds,
            1,
            3600,
            nameof(options.DefaultTimeoutSeconds),
            failures);
        RequireRange(
            options.MaximumTimeoutSeconds,
            1,
            3600,
            nameof(options.MaximumTimeoutSeconds),
            failures);
        if (options.DefaultTimeoutSeconds > options.MaximumTimeoutSeconds)
        {
            failures.Add("DefaultTimeoutSeconds must not exceed MaximumTimeoutSeconds.");
        }

        RequireRange(
            options.TerminationGraceSeconds,
            1,
            60,
            nameof(options.TerminationGraceSeconds),
            failures);
        RequirePositive(
            options.MaximumStandardOutputBytes,
            nameof(options.MaximumStandardOutputBytes),
            failures);
        RequirePositive(
            options.MaximumStandardErrorBytes,
            nameof(options.MaximumStandardErrorBytes),
            failures);
        RequirePositive(
            options.MaximumCombinedOutputBytes,
            nameof(options.MaximumCombinedOutputBytes),
            failures);
        if (options.MaximumCombinedOutputBytes < options.MaximumStandardOutputBytes ||
            options.MaximumCombinedOutputBytes < options.MaximumStandardErrorBytes)
        {
            failures.Add(
                "MaximumCombinedOutputBytes must not be less than either stream limit.");
        }

        ValidateRoot(options.AllowedScriptRoot, nameof(options.AllowedScriptRoot), failures);
        ValidateRoot(options.WorkingRoot, nameof(options.WorkingRoot), failures);
        if (options.AllowedScriptRoot is not null &&
            options.WorkingRoot is not null &&
            Path.IsPathFullyQualified(options.AllowedScriptRoot) &&
            Path.IsPathFullyQualified(options.WorkingRoot))
        {
            var allowedRoot = NormalizeDirectory(options.AllowedScriptRoot);
            var workingRoot = NormalizeDirectory(options.WorkingRoot);
            if (IsSameOrDescendant(allowedRoot, workingRoot) ||
                IsSameOrDescendant(workingRoot, allowedRoot))
            {
                failures.Add(
                    "AllowedScriptRoot and WorkingRoot must not overlap or be nested.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRoot(
        string? path,
        string optionName,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            failures.Add($"{optionName} must be a fully qualified path.");
            return;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            failures.Add($"{optionName} must be a local non-device path.");
            return;
        }

        var root = Path.GetPathRoot(path) ?? string.Empty;
        if (path[root.Length..].Contains(':', StringComparison.Ordinal))
        {
            failures.Add($"{optionName} must not contain an alternate data stream.");
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrDescendant(string root, string candidate)
    {
        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static void RequirePositive(
        int value,
        string optionName,
        ICollection<string> failures)
    {
        if (value <= 0)
        {
            failures.Add($"{optionName} must be positive.");
        }
    }

    private static void RequireRange(
        int value,
        int minimum,
        int maximum,
        string optionName,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{optionName} must be between {minimum} and {maximum}.");
        }
    }
}
