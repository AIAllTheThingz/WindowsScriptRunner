using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;

namespace WindowsScriptRunner.Worker;

public sealed class JobWorkHandlerRegistry
{
    private readonly IReadOnlyDictionary<JobWorkKind, IJobWorkHandler> _handlers;

    public JobWorkHandlerRegistry(IEnumerable<IJobWorkHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var registry = new Dictionary<JobWorkKind, IJobWorkHandler>();
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (!Enum.IsDefined(handler.WorkKind))
            {
                throw new InvalidOperationException(
                    $"Handler work kind '{handler.WorkKind}' is not defined.");
            }

            if (!registry.TryAdd(handler.WorkKind, handler))
            {
                throw new InvalidOperationException(
                    $"A handler is already registered for work kind '{handler.WorkKind}'.");
            }
        }

        _handlers = registry;
        SupportedWorkKinds = registry.Keys.ToHashSet();
    }

    public IReadOnlySet<JobWorkKind> SupportedWorkKinds { get; }

    public IJobWorkHandler GetRequired(JobWorkKind workKind) =>
        _handlers.TryGetValue(workKind, out var handler)
            ? handler
            : throw new InvalidOperationException(
                $"No work handler is registered for '{workKind}'.");
}
