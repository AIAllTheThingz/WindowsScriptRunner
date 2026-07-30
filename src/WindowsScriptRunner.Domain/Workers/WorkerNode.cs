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

    internal static WorkerNode Rehydrate(
        WorkerNodeId id,
        string name,
        bool isEnabled,
        DateTimeOffset registeredUtc,
        DateTimeOffset? lastHeartbeatUtc,
        IEnumerable<WorkerCapability> capabilities)
    {
        if (lastHeartbeatUtc is not null && lastHeartbeatUtc < registeredUtc)
        {
            throw new DomainValidationException(
                "Worker heartbeat timestamp cannot precede registration.");
        }

        var worker = new WorkerNode(id, name, registeredUtc, isEnabled)
        {
            LastHeartbeatUtc = lastHeartbeatUtc,
        };

        foreach (var capability in capabilities ??
            throw new DomainValidationException("Worker capabilities are required."))
        {
            ArgumentNullException.ThrowIfNull(capability);
            if (worker._capabilities.Any(
                existing => string.Equals(
                    existing.Name,
                    capability.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new DomainValidationException(
                    $"Worker capability '{capability.Name}' is duplicated in persisted state.");
            }

            worker._capabilities.Add(capability);
        }

        return worker;
    }

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

    public bool SynchronizeCapabilities(IEnumerable<WorkerCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var proposed = capabilities.ToArray();
        if (proposed.Any(capability => capability is null))
        {
            throw new DomainValidationException("Worker capabilities cannot contain null entries.");
        }

        var duplicate = proposed
            .GroupBy(capability => capability.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainValidationException(
                $"Worker capability '{duplicate.Key}' is duplicated.");
        }

        var unchanged = _capabilities.Count == proposed.Length &&
            _capabilities.All(existing => proposed.Any(candidate =>
                string.Equals(existing.Name, candidate.Name, StringComparison.OrdinalIgnoreCase) &&
                existing == candidate));
        if (unchanged)
        {
            return false;
        }

        _capabilities.Clear();
        _capabilities.AddRange(proposed);
        return true;
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

    public bool IsLive(DateTimeOffset now, TimeSpan staleAfter)
    {
        if (staleAfter <= TimeSpan.Zero)
        {
            throw new DomainValidationException("Worker staleness duration must be positive.");
        }

        return IsEnabled &&
            LastHeartbeatUtc is not null &&
            LastHeartbeatUtc >= now - staleAfter;
    }
}
