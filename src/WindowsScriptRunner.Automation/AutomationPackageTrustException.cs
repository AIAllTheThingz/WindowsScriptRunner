namespace WindowsScriptRunner.Automation;

internal sealed class AutomationPackageTrustException : Exception
{
    internal AutomationPackageTrustException(string message)
        : base(message)
    {
    }
}
