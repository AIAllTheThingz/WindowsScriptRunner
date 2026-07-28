using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Credentials;

public sealed class CredentialReference
{
    public CredentialReference(
        CredentialReferenceId id,
        string providerType,
        string externalIdentifier,
        string displayName,
        DateTimeOffset createdUtc,
        UserIdentity createdBy,
        bool isEnabled = true)
    {
        Id = id ?? throw new DomainValidationException("Credential reference identifier is required.");
        ProviderType = Validate(providerType, nameof(ProviderType), 200);
        ExternalIdentifier = ValidateExternalIdentifier(externalIdentifier);
        DisplayName = Validate(displayName, nameof(DisplayName), 200);
        IsEnabled = isEnabled;
        CreatedUtc = createdUtc;
        CreatedBy = createdBy ?? throw new DomainValidationException("Credential creator is required.");
    }

    public CredentialReferenceId Id { get; }
    public string ProviderType { get; }
    public string ExternalIdentifier { get; }
    public string DisplayName { get; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedUtc { get; }
    public UserIdentity CreatedBy { get; }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;

    public override string ToString() => $"{DisplayName} ({ProviderType})";

    private static string Validate(string value, string fieldName, int maximumLength)
    {
        var normalized = Guard.RequiredTrimmed(value, fieldName, maximumLength);
        Guard.NoControlCharacters(normalized, fieldName);
        return normalized;
    }

    private static string ValidateExternalIdentifier(string value)
    {
        var identifier = Validate(value, nameof(ExternalIdentifier), 500);
        string[] prohibitedMarkers =
        [
            "password=",
            "pwd=",
            "user id=",
            "connectionstring=",
            "secret=",
        ];
        if (prohibitedMarkers.Any(
            marker => identifier.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException(
                "External credential identifier appears to contain embedded credential material.");
        }

        return identifier;
    }
}
