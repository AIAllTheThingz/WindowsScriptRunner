using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Application.Reports;

public static class LocalHostInventoryWorkerTargetPolicy
{
    public static bool RequiresTarget(ScriptVersionId scriptVersionId)
    {
        ArgumentNullException.ThrowIfNull(scriptVersionId);
        return scriptVersionId == LocalHostInventoryPackagePolicy.VersionId;
    }

    public static TargetName CreateTarget(WorkerNodeId workerNodeId)
    {
        ArgumentNullException.ThrowIfNull(workerNodeId);
        return new TargetName($"worker:{workerNodeId.Value:D}");
    }

    public static bool HasExactTarget(Job job, WorkerNodeId workerNodeId)
    {
        ArgumentNullException.ThrowIfNull(job);
        var target = CreateTarget(workerNodeId);
        return job.Targets.Count == 1 && job.Targets.Single().Name.Equals(target);
    }
}
