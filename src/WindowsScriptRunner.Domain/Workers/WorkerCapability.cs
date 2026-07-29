using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.Domain.Workers;

public sealed record WorkerCapability
{
    public WorkerCapability(string name, string value)
    {
        Name = Guard.RequiredTrimmed(name, nameof(Name), 200);
        Value = Guard.RequiredTrimmed(value, nameof(Value), 200);
        Guard.NoControlCharacters(Name, nameof(Name));
        Guard.NoControlCharacters(Value, nameof(Value));
    }

    public string Name { get; }
    public string Value { get; }
}
