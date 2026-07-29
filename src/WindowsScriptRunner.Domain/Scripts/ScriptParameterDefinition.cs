using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Scripts;

public sealed class ScriptParameterDefinition
{
    private static readonly Regex ParameterNamePattern = new(
        @"\A[A-Za-z_][A-Za-z0-9_]{0,99}\z",
        RegexOptions.CultureInvariant);

    private readonly IReadOnlyCollection<string> _allowedValues;

    public ScriptParameterDefinition(
        ScriptParameterDefinitionId id,
        string name,
        string displayName,
        string? description,
        ScriptParameterType parameterType,
        bool isRequired,
        string? defaultValue,
        IEnumerable<string>? allowedValues,
        bool isSensitive)
    {
        Id = id ?? throw new DomainValidationException("Script parameter definition identifier is required.");
        Name = ValidateName(name);
        DisplayName = Guard.RequiredTrimmed(displayName, nameof(DisplayName), 200);
        Description = NormalizeOptional(description, nameof(Description), 1000);
        ParameterType = EnumGuard.RequireDefined(parameterType, nameof(ParameterType));
        IsRequired = isRequired;
        IsSensitive = isSensitive;
        DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue;
        _allowedValues = NormalizeAllowedValues(allowedValues);

        ValidateDefinition();
    }

    public ScriptParameterDefinitionId Id { get; }
    public string Name { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public ScriptParameterType ParameterType { get; }
    public bool IsRequired { get; }
    public string? DefaultValue { get; }
    public IReadOnlyCollection<string> AllowedValues => _allowedValues;
    public bool IsSensitive { get; }

    public void ValidateSerializedValue(string? serializedValue)
    {
        if (string.IsNullOrWhiteSpace(serializedValue))
        {
            if (IsRequired && DefaultValue is null)
            {
                throw new InvalidJobParameterException(Name, "a value is required.");
            }

            return;
        }

        var valid = ParameterType switch
        {
            ScriptParameterType.String => true,
            ScriptParameterType.StringArray => IsStringArray(serializedValue),
            ScriptParameterType.Integer => int.TryParse(
                serializedValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _),
            ScriptParameterType.Boolean => bool.TryParse(serializedValue, out _),
            ScriptParameterType.DateTime => DateTimeOffset.TryParse(
                serializedValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _),
            ScriptParameterType.Enum => _allowedValues.Contains(serializedValue, StringComparer.OrdinalIgnoreCase),
            ScriptParameterType.SecureReference => IsCredentialReferenceId(serializedValue),
            _ => false,
        };

        if (!valid)
        {
            throw new InvalidJobParameterException(Name, $"the supplied {ParameterType} representation is invalid.");
        }
    }

    private void ValidateDefinition()
    {
        if (ParameterType == ScriptParameterType.Enum)
        {
            if (_allowedValues.Count == 0)
            {
                throw new InvalidParameterDefinitionException("Enum parameters require at least one allowed value.");
            }
        }
        else if (_allowedValues.Count > 0)
        {
            throw new InvalidParameterDefinitionException("Allowed values may only be used with Enum parameters.");
        }

        if (ParameterType == ScriptParameterType.SecureReference && !IsSensitive)
        {
            throw new InvalidParameterDefinitionException("SecureReference parameters must be sensitive.");
        }

        if (IsSensitive && DefaultValue is not null)
        {
            throw new InvalidParameterDefinitionException("Sensitive parameters cannot define raw default values.");
        }

        if (DefaultValue is not null)
        {
            try
            {
                ValidateSerializedValue(DefaultValue);
            }
            catch (InvalidJobParameterException exception)
            {
                throw new InvalidParameterDefinitionException(
                    $"Default value for '{Name}' is invalid: {exception.Message}");
            }
        }
    }

    private static string ValidateName(string value)
    {
        var name = Guard.RequiredTrimmed(value, "Parameter name", 100);
        if (!ParameterNamePattern.IsMatch(name))
        {
            throw new InvalidParameterDefinitionException(
                "Parameter names must start with a letter or underscore and contain only letters, numbers, or underscores.");
        }

        return name;
    }

    private static string? NormalizeOptional(string? value, string fieldName, int maximumLength)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = Guard.OptionalTrimmed(value, fieldName, maximumLength);
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsStringArray(string serializedValue)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string?[]>(serializedValue);
            return values is not null && values.All(value => value is not null);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsCredentialReferenceId(string value) =>
        CredentialReferenceId.TryParse(value, out var id) &&
        string.Equals(value.Trim(), id!.ToString(), StringComparison.Ordinal);

    private static IReadOnlyCollection<string> NormalizeAllowedValues(IEnumerable<string>? values)
    {
        var normalized = (values ?? [])
            .Select(value => Guard.RequiredTrimmed(value, "Allowed value", 200))
            .ToArray();

        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new InvalidParameterDefinitionException("Allowed values must be unique.");
        }

        return Array.AsReadOnly(normalized);
    }
}
