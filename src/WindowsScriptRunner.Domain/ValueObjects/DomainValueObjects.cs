using System.Globalization;
using System.Text.RegularExpressions;
using WindowsScriptRunner.Domain.Exceptions;

namespace WindowsScriptRunner.Domain.ValueObjects;

public sealed record ScriptName
{
    private static readonly Regex ValidPattern = new(
        @"\A[A-Za-z0-9_.-]{3,100}\z",
        RegexOptions.CultureInvariant);

    public ScriptName(string value)
    {
        Value = Guard.RequiredTrimmed(value, nameof(ScriptName), 100);
        if (!ValidPattern.IsMatch(Value) || Value.Contains("..", StringComparison.Ordinal))
        {
            throw new DomainValidationException(
                "Script name must be 3-100 letters, numbers, hyphens, underscores, or periods and cannot contain '..'.");
        }
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct ScriptVersionNumber
{
    public ScriptVersionNumber(int major, int minor, int patch)
    {
        if (major < 0 || minor < 0 || patch < 0)
        {
            throw new DomainValidationException("Semantic version components cannot be negative.");
        }

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public static ScriptVersionNumber Parse(string value)
    {
        var parts = value?.Split('.', StringSplitOptions.None);
        if (parts is not { Length: 3 } ||
            !parts.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            throw new DomainValidationException("Script version must use the major.minor.patch format.");
        }

        return new ScriptVersionNumber(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed record UserIdentity
{
    public UserIdentity(string value)
    {
        Value = Guard.RequiredTrimmed(value, nameof(UserIdentity), 256);
        Guard.NoControlCharacters(Value, nameof(UserIdentity));
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public sealed class TargetName : IEquatable<TargetName>
{
    private static readonly string[] CommandSeparators = [";", "&&", "||", "|", "`", "$(", "\r", "\n"];

    public TargetName(string value)
    {
        Value = Guard.RequiredTrimmed(value, nameof(TargetName), 255);
        Guard.NoControlCharacters(Value, nameof(TargetName));
        if (CommandSeparators.Any(Value.Contains))
        {
            throw new DomainValidationException("Target name contains a prohibited command separator.");
        }
    }

    public string Value { get; }
    public bool Equals(TargetName? other) =>
        other is not null && StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);
    public override bool Equals(object? obj) => obj is TargetName other && Equals(other);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
    public override string ToString() => Value;
}

public sealed record ChangeReference
{
    public ChangeReference(string value)
    {
        Value = Guard.RequiredTrimmed(value, nameof(ChangeReference), 100);
        Guard.NoControlCharacters(Value, nameof(ChangeReference));
    }

    public string Value { get; }
    public override string ToString() => Value;
}

internal static class Guard
{
    public static string RequiredTrimmed(string? value, string fieldName, int maximumLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainValidationException($"{fieldName} is required.");
        }

        if (trimmed.Length > maximumLength)
        {
            throw new DomainValidationException($"{fieldName} cannot exceed {maximumLength} characters.");
        }

        return trimmed;
    }

    public static string OptionalTrimmed(string? value, string fieldName, int maximumLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length > maximumLength)
        {
            throw new DomainValidationException($"{fieldName} cannot exceed {maximumLength} characters.");
        }

        return trimmed;
    }

    public static void NoControlCharacters(string value, string fieldName)
    {
        if (value.Any(char.IsControl))
        {
            throw new DomainValidationException($"{fieldName} cannot contain control characters.");
        }
    }
}
