using System.Text.RegularExpressions;
using WindowsScriptRunner.Domain.Exceptions;

namespace WindowsScriptRunner.Domain.Jobs;

public sealed class JobParameter
{
    private static readonly Regex NamePattern = new(
        @"\A[A-Za-z_][A-Za-z0-9_]{0,99}\z",
        RegexOptions.CultureInvariant);

    public JobParameter(string name, string? serializedValue)
    {
        Name = ValidateName(name);
        SerializedValue = string.IsNullOrWhiteSpace(serializedValue)
            ? throw new InvalidJobParameterException(
                Name,
                "an explicit parameter binding requires a value.")
            : serializedValue;
    }

    public string Name { get; }
    public string? SerializedValue { get; }

    public override string ToString() => $"{Name}=[VALUE OMITTED]";

    internal static string ValidateName(string value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || !NamePattern.IsMatch(name))
        {
            throw new InvalidJobParameterException(
                value ?? "(null)",
                "name must be a valid PowerShell-style parameter identifier.");
        }

        return name;
    }
}
