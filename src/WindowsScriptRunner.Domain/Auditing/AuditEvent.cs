using System.Collections.ObjectModel;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Auditing;

public sealed class AuditEvent
{
    public AuditEvent(
        AuditEventId id,
        string eventType,
        string entityType,
        string entityId,
        UserIdentity actor,
        DateTimeOffset occurredUtc,
        string summary,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        Id = id ?? throw new DomainValidationException("Audit event identifier is required.");
        EventType = ValidateText(eventType, nameof(EventType), 200);
        EntityType = ValidateText(entityType, nameof(EntityType), 200);
        EntityId = ValidateText(entityId, nameof(EntityId), 500);
        Actor = actor ?? throw new DomainValidationException("Audit actor is required.");
        OccurredUtc = occurredUtc;
        Summary = ValidateText(summary, nameof(Summary), 2000);
        Properties = CopyProperties(properties);
    }

    public AuditEventId Id { get; }
    public string EventType { get; }
    public string EntityType { get; }
    public string EntityId { get; }
    public UserIdentity Actor { get; }
    public DateTimeOffset OccurredUtc { get; }
    public string Summary { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }

    private static string ValidateText(string value, string fieldName, int maximumLength)
    {
        var normalized = Guard.RequiredTrimmed(value, fieldName, maximumLength);
        Guard.NoControlCharacters(normalized, fieldName);
        return normalized;
    }

    private static IReadOnlyDictionary<string, string> CopyProperties(
        IReadOnlyDictionary<string, string>? properties)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in properties ?? new Dictionary<string, string>())
        {
            var key = ValidateText(pair.Key, "Audit property key", 200);
            var value = ValidateText(pair.Value, "Audit property value", 2000);
            if (LooksSensitive(key))
            {
                throw new DomainValidationException("Sensitive values cannot be stored in audit properties.");
            }

            copy.Add(key, value);
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }

    private static bool LooksSensitive(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase);
}
