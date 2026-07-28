using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Jobs;

public sealed class JobTarget
{
    public JobTarget(TargetName name, DateTimeOffset addedUtc, UserIdentity addedBy)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        AddedUtc = addedUtc;
        AddedBy = addedBy ?? throw new ArgumentNullException(nameof(addedBy));
    }

    public TargetName Name { get; }
    public DateTimeOffset AddedUtc { get; }
    public UserIdentity AddedBy { get; }
}
