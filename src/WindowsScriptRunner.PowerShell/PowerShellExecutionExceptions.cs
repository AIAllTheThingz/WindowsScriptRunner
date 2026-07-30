namespace WindowsScriptRunner.PowerShell;

public class PowerShellExecutionException : Exception
{
    public PowerShellExecutionException(string message)
        : base(message)
    {
    }

    public PowerShellExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PowerShellRuntimeNotFoundException : PowerShellExecutionException
{
    public PowerShellRuntimeNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class PowerShellRuntimeValidationException : PowerShellExecutionException
{
    public PowerShellRuntimeValidationException(string message)
        : base(message)
    {
    }

    public PowerShellRuntimeValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PowerShellScriptTrustException : PowerShellExecutionException
{
    public PowerShellScriptTrustException(string message)
        : base(message)
    {
    }

    public PowerShellScriptTrustException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PowerShellProcessStartException : PowerShellExecutionException
{
    public PowerShellProcessStartException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PowerShellProcessTerminationException : PowerShellExecutionException
{
    public PowerShellProcessTerminationException(string message)
        : base(message)
    {
    }

    public PowerShellProcessTerminationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
