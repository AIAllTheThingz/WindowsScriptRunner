using WindowsScriptRunner.Application.Exceptions;

namespace WindowsScriptRunner.Infrastructure.Persistence;

public sealed class PersistenceUnavailableException : ApplicationExceptionBase
{
    public PersistenceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PersistenceOperationException : ApplicationExceptionBase
{
    public PersistenceOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
