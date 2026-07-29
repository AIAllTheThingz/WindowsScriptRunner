using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Scripts;

public sealed class ScriptDefinition
{
    private readonly List<ScriptVersion> _versions = [];

    private ScriptDefinition(
        ScriptDefinitionId id,
        ScriptName name,
        string displayName,
        string description,
        RiskLevel riskLevel,
        UserIdentity createdBy,
        DateTimeOffset createdUtc)
    {
        Id = id ?? throw new DomainValidationException("Script definition identifier is required.");
        Name = name ?? throw new DomainValidationException("Script name is required.");
        DisplayName = ValidateDisplayName(displayName);
        Description = ValidateDescription(description);
        RiskLevel = EnumGuard.RequireDefined(riskLevel, nameof(RiskLevel));
        CreatedBy = createdBy ?? throw new DomainValidationException("Script creator is required.");
        CreatedUtc = createdUtc;
        UpdatedUtc = createdUtc;
        IsEnabled = true;
    }

    public ScriptDefinitionId Id { get; }
    public ScriptName Name { get; }
    public string DisplayName { get; private set; }
    public string Description { get; private set; }
    public RiskLevel RiskLevel { get; }
    public bool IsEnabled { get; private set; }
    public UserIdentity CreatedBy { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset UpdatedUtc { get; private set; }
    public IReadOnlyCollection<ScriptVersion> Versions => _versions.AsReadOnly();

    public static ScriptDefinition Create(
        ScriptDefinitionId id,
        ScriptName name,
        string displayName,
        string description,
        RiskLevel riskLevel,
        UserIdentity createdBy,
        DateTimeOffset createdUtc) =>
        new(id, name, displayName, description, riskLevel, createdBy, createdUtc);

    internal static ScriptDefinition Rehydrate(
        ScriptDefinitionId id,
        ScriptName name,
        string displayName,
        string description,
        RiskLevel riskLevel,
        bool isEnabled,
        UserIdentity createdBy,
        DateTimeOffset createdUtc,
        DateTimeOffset updatedUtc,
        IEnumerable<ScriptVersion> versions)
    {
        if (updatedUtc < createdUtc)
        {
            throw new DomainValidationException(
                "Script definition update timestamp cannot precede creation.");
        }

        var definition = new ScriptDefinition(
            id,
            name,
            displayName,
            description,
            riskLevel,
            createdBy,
            createdUtc)
        {
            IsEnabled = isEnabled,
            UpdatedUtc = updatedUtc,
        };

        foreach (var version in versions ?? throw new DomainValidationException("Script versions are required."))
        {
            ArgumentNullException.ThrowIfNull(version);
            if (definition._versions.Any(existing => existing.Id == version.Id))
            {
                throw new InvalidScriptVersionException(
                    $"Script version identifier {version.Id} is duplicated in persisted state.");
            }

            if (definition._versions.Any(existing => existing.Version == version.Version))
            {
                throw new InvalidScriptVersionException(
                    $"Script version {version.Version} is duplicated in persisted state.");
            }

            if (version.CreatedUtc < createdUtc || version.CreatedUtc > updatedUtc)
            {
                throw new InvalidScriptVersionException(
                    "Persisted script version timestamp must fall within the script definition lifetime.");
            }

            definition._versions.Add(version);
        }

        return definition;
    }

    public void UpdateDetails(string displayName, string description, DateTimeOffset updatedUtc)
    {
        EnsureTimestamp(updatedUtc);
        var validatedDisplayName = ValidateDisplayName(displayName);
        var validatedDescription = ValidateDescription(description);

        DisplayName = validatedDisplayName;
        Description = validatedDescription;
        UpdatedUtc = updatedUtc;
    }

    public void Enable(DateTimeOffset updatedUtc)
    {
        EnsureTimestamp(updatedUtc);
        IsEnabled = true;
        UpdatedUtc = updatedUtc;
    }

    public void Disable(DateTimeOffset updatedUtc)
    {
        EnsureTimestamp(updatedUtc);
        IsEnabled = false;
        UpdatedUtc = updatedUtc;
    }

    public void AddVersion(ScriptVersion version, DateTimeOffset updatedUtc)
    {
        ArgumentNullException.ThrowIfNull(version);
        EnsureTimestamp(updatedUtc);
        if (version.CreatedUtc < CreatedUtc || version.CreatedUtc > updatedUtc)
        {
            throw new InvalidScriptVersionException(
                "Script version timestamp must fall within the script definition lifetime.");
        }

        if (_versions.Any(existing => existing.Version == version.Version))
        {
            throw new InvalidScriptVersionException($"Script version {version.Version} already exists.");
        }

        if (_versions.Any(existing => existing.Id == version.Id))
        {
            throw new InvalidScriptVersionException($"Script version identifier {version.Id} already exists.");
        }

        _versions.Add(version);
        UpdatedUtc = updatedUtc;
    }

    public ScriptVersion GetVersion(ScriptVersionId id)
    {
        if (id is null)
        {
            throw new DomainValidationException("Script version identifier is required.");
        }

        return _versions.FirstOrDefault(version => version.Id == id)
            ?? throw new InvalidScriptVersionException($"Script version '{id}' does not belong to this definition.");
    }

    private void EnsureTimestamp(DateTimeOffset updatedUtc)
    {
        if (updatedUtc < UpdatedUtc)
        {
            throw new DomainValidationException("Update timestamps cannot move backward.");
        }
    }

    private static string ValidateDisplayName(string value) =>
        Guard.RequiredTrimmed(value, nameof(DisplayName), 200);

    private static string ValidateDescription(string value) =>
        Guard.OptionalTrimmed(value, nameof(Description), 2000);
}
