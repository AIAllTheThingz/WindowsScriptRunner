namespace WindowsScriptRunner.PowerShell;

internal interface IPowerShellArgumentValidator
{
    void Validate(
        TrustedPowerShellScript script,
        IReadOnlyList<PowerShellArgument> arguments);
}

internal sealed class PowerShellArgumentValidator : IPowerShellArgumentValidator
{
    public void Validate(
        TrustedPowerShellScript script,
        IReadOnlyList<PowerShellArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count > PowerShellExecutionOptions.MaximumArgumentCount)
        {
            throw new PowerShellScriptTrustException(
                "The PowerShell argument count exceeds the supported maximum.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in arguments)
        {
            if (argument is null ||
                string.IsNullOrWhiteSpace(argument.Name) ||
                !PowerShellScriptTrustValidator.IsValidParameterName(argument.Name) ||
                !script.AllowedParameterNames.Contains(argument.Name))
            {
                throw new PowerShellScriptTrustException(
                    "A PowerShell argument name is not allowed.");
            }

            if (!names.Add(argument.Name))
            {
                throw new PowerShellScriptTrustException(
                    "PowerShell argument names must be unique.");
            }

            if (argument.Value is null ||
                argument.Value.Contains('\0', StringComparison.Ordinal) ||
                argument.Value.StartsWith('-') ||
                argument.Value.Length > PowerShellExecutionOptions.MaximumArgumentValueLength)
            {
                throw new PowerShellScriptTrustException(
                    "A PowerShell argument value is invalid.");
            }

            if (argument.Sensitivity != PowerShellArgumentSensitivity.NonSensitive)
            {
                throw new PowerShellScriptTrustException(
                    "Sensitive values are not supported by the Phase 5 command line.");
            }
        }
    }
}
