using Microsoft.Extensions.Options;
using WindowsScriptRunner.Domain.Identifiers;

namespace WindowsScriptRunner.Worker;

public sealed class WorkerIdentity
{
    public WorkerIdentity(IOptions<WorkerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value;
        NodeId = new WorkerNodeId(
            configured.NodeId == Guid.Empty
                ? Guid.NewGuid()
                : configured.NodeId);
        Name = configured.Name.Trim();
    }

    public WorkerNodeId NodeId { get; }
    public string Name { get; }
}
