namespace WindowsScriptRunner.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}

public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message)
        : base(message)
    {
    }
}

public sealed class InvalidJobStateTransitionException : DomainException
{
    public InvalidJobStateTransitionException(JobStatus current, JobStatus requested)
        : base($"A job cannot transition from {current} to {requested}.")
    {
    }
}

public sealed class DuplicateJobTargetException : DomainException
{
    public DuplicateJobTargetException(string targetName)
        : base($"The target '{targetName}' is already part of the job.")
    {
    }
}

public sealed class InvalidScriptVersionException : DomainException
{
    public InvalidScriptVersionException(string message)
        : base(message)
    {
    }
}

public sealed class InvalidParameterDefinitionException : DomainException
{
    public InvalidParameterDefinitionException(string message)
        : base(message)
    {
    }
}

public sealed class InvalidJobParameterException : DomainException
{
    public InvalidJobParameterException(string parameterName, string message)
        : base($"Parameter '{parameterName}' is invalid: {message}")
    {
    }
}
