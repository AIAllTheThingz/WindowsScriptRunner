using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Domain;

namespace WindowsScriptRunner.Worker;

public sealed class JobWorkHandlerRegistry
{
    private readonly IReadOnlyDictionary<JobWorkRoute, IJobWorkHandler> _handlers;

    public JobWorkHandlerRegistry(IEnumerable<IJobWorkHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var registry = new Dictionary<JobWorkRoute, IJobWorkHandler>();
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(handler.SupportedRoutes);
            foreach (var route in handler.SupportedRoutes)
            {
                if (route is null ||
                    route.ScriptVersionId is null ||
                    !Enum.IsDefined(route.WorkKind))
                {
                    throw new InvalidOperationException(
                        "Handler routes must contain a defined work kind and script version.");
                }

                if (!registry.TryAdd(route, handler))
                {
                    throw new InvalidOperationException(
                        $"A handler is already registered for route '{route}'.");
                }
            }
        }

        _handlers = registry;
        SupportedRoutes = registry.Keys.ToHashSet();
    }

    public IReadOnlySet<JobWorkRoute> SupportedRoutes { get; }

    public IJobWorkHandler GetRequired(JobWorkRoute route) =>
        _handlers.TryGetValue(route, out var handler)
            ? handler
            : throw new InvalidOperationException(
                $"No work handler is registered for route '{route}'.");
}
