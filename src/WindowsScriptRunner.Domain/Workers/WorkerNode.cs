using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Workers;

public sealed class WorkerNode
{
    private readonly List<WorkerCapability> _capabilities = [];

    public WorkerNode(
        WorkerNodeId id,
        string name,
        DateTimeOffset registeredUtc,
        bool isEnabled = true)
    {
        Id = id ?? throw new DomainValidationException("Worker node identifier is required.");
        Name = Guard.RequiredTrimmed(name, nameof(Name), 200);
        RegisteredUtc = registeredUtc;
        IsEnabled = isEnabled;
    }

    public WorkerNodeId Id { get; }
    public string Name { get; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset RegisteredUtc { get; }
    public DateTimeOffset? LastHeartbeatUtc { get; private set; }
    public IReadOnlyCollection<WorkerCapability> Capabilities => _capabilities.AsReadOnly();

    public void RegisterCapability(WorkerCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (_capabilities.Any(
            existing => string.Equals(existing.Name, capability.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException($"Worker capability '{capability.Name}' is already registered.");
        }

        _capabilities.Add(capability);
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;

    public void RecordHeartbeat(DateTimeOffset heartbeatUtc)
    {
        if (heartbeatUtc < RegisteredUtc ||
            (LastHeartbeatUtc is not null && heartbeatUtc < LastHeartbeatUtc))
        {
            throw new DomainValidationException("Worker heartbeat timestamps cannot move backward.");
        }

        LastHeartbeatUtc = heartbeatUtc;
    }
}
