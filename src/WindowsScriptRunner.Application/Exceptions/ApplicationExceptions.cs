namespace WindowsScriptRunner.Application.Exceptions;

public class ApplicationExceptionBase : Exception
{
    public ApplicationExceptionBase(string message)
        : base(message)
    {
    }

    public ApplicationExceptionBase(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class EntityNotFoundException : ApplicationExceptionBase
{
    public EntityNotFoundException(string entityType, string identifier)
        : base($"{entityType} '{identifier}' was not found.")
    {
    }
}

public sealed class ApplicationValidationException : ApplicationExceptionBase
{
    public ApplicationValidationException(string message)
        : base(message)
    {
    }

    public ApplicationValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ApplicationConflictException : ApplicationExceptionBase
{
    public ApplicationConflictException(string message)
        : base(message)
    {
    }

    public ApplicationConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class UnauthorizedOperationException : ApplicationExceptionBase
{
    public UnauthorizedOperationException(string message)
        : base(message)
    {
    }
}
