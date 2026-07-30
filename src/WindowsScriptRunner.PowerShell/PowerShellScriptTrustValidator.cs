using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.PowerShell;

internal interface IPowerShellScriptTrustValidator
{
    string ValidateImmediatelyBeforeLaunch(TrustedPowerShellScript script);
}

internal sealed partial class PowerShellScriptTrustValidator(
    IOptions<PowerShellExecutionOptions> options) : IPowerShellScriptTrustValidator
{
    private readonly string _allowedRoot = NormalizeRoot(
        options.Value.AllowedScriptRoot ??
        throw new PowerShellScriptTrustException(
            "The trusted script root is not configured."));

    public string ValidateImmediatelyBeforeLaunch(TrustedPowerShellScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        ValidateArtifactMetadata(script);
        var originalPath = script.CanonicalPath;
        if (!Path.IsPathFullyQualified(originalPath) ||
            IsUncOrDevicePath(originalPath))
        {
            throw new PowerShellScriptTrustException(
                "The trusted script path must be a fully qualified local path.");
        }

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(originalPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PowerShellScriptTrustException(
                "The trusted script path is invalid.",
                exception);
        }

        if (!string.Equals(originalPath, canonicalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new PowerShellScriptTrustException(
                "The trusted script path must already be canonical.");
        }

        if (!canonicalPath.StartsWith(_allowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new PowerShellScriptTrustException(
                "The trusted script is outside the configured allowed root.");
        }

        if (HasAlternateDataStream(canonicalPath) ||
            !string.Equals(
                Path.GetExtension(canonicalPath),
                ".ps1",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(canonicalPath) ||
            Directory.Exists(canonicalPath))
        {
            throw new PowerShellScriptTrustException(
                "The trusted script path does not identify an allowed script file.");
        }

        RejectReparsePoints(canonicalPath);
        VerifyHash(canonicalPath, script.Sha256);
        return canonicalPath;
    }

    internal static bool IsValidParameterName(string name) =>
        ParameterNamePattern().IsMatch(name);

    private static string NormalizeRoot(string path)
    {
        if (!Path.IsPathFullyQualified(path) || IsUncOrDevicePath(path))
        {
            throw new PowerShellScriptTrustException(
                "The trusted script root must be a fully qualified local path.");
        }

        var canonical = Path.GetFullPath(path);
        return Path.EndsInDirectorySeparator(canonical)
            ? canonical
            : canonical + Path.DirectorySeparatorChar;
    }

    private static void ValidateArtifactMetadata(TrustedPowerShellScript script)
    {
        if (string.IsNullOrWhiteSpace(script.ArtifactName) ||
            script.ArtifactName.Length > 100 ||
            script.AllowedParameterNames.Count > PowerShellExecutionOptions.MaximumArgumentCount ||
            script.AllowedParameterNames.Any(name => !IsValidParameterName(name)))
        {
            throw new PowerShellScriptTrustException(
                "The trusted script metadata is invalid.");
        }
    }

    private static void VerifyHash(string path, string expectedHash)
    {
        byte[] expected;
        try
        {
            if (expectedHash.Length != 64)
            {
                throw new FormatException();
            }

            expected = Convert.FromHexString(expectedHash);
        }
        catch (FormatException exception)
        {
            throw new PowerShellScriptTrustException(
                "The trusted script hash is invalid.",
                exception);
        }

        byte[] actual;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            actual = SHA256.HashData(stream);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new PowerShellScriptTrustException(
                "The trusted script could not be hashed.",
                exception);
        }

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new PowerShellScriptTrustException(
                "The trusted script hash does not match.");
        }
    }

    private static void RejectReparsePoints(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            throw new PowerShellScriptTrustException(
                "The trusted script path has no local root.");
        }

        var current = root;
        foreach (var component in path[root.Length..].Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new PowerShellScriptTrustException(
                    "The trusted script path could not be inspected.",
                    exception);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new PowerShellScriptTrustException(
                    "The trusted script path contains a reparse point.");
            }
        }
    }

    private static bool HasAlternateDataStream(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return path[root.Length..].Contains(':', StringComparison.Ordinal);
    }

    private static bool IsUncOrDevicePath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
        path.StartsWith(@"\\.\", StringComparison.Ordinal);

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]{0,99}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNamePattern();
}
