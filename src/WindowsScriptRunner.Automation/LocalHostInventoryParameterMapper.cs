using System.Globalization;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.Automation;

internal static class LocalHostInventoryParameterMapper
{
    internal static IReadOnlyList<PowerShellArgument> Map(
        Job job,
        ScriptVersion version,
        IReadOnlySet<string> allowedParameterNames)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(allowedParameterNames);

        foreach (var definition in version.ParameterDefinitions)
        {
            if (definition.IsSensitive ||
                definition.ParameterType == ScriptParameterType.SecureReference ||
                !allowedParameterNames.Contains(definition.Name))
            {
                throw new AutomationPackageTrustException(
                    "The package parameter definition is not permitted by the reviewed catalog.");
            }
        }

        var arguments = new List<PowerShellArgument>();
        foreach (var parameter in job.Parameters.OrderBy(
            parameter => parameter.Name,
            StringComparer.Ordinal))
        {
            var definition = version.GetParameterDefinition(parameter.Name);
            definition.ValidateSerializedValue(parameter.SerializedValue);
            if (!allowedParameterNames.Contains(definition.Name) ||
                definition.IsSensitive ||
                definition.ParameterType == ScriptParameterType.SecureReference)
            {
                throw new AutomationPackageTrustException(
                    "A job parameter is not permitted by the reviewed catalog.");
            }

            arguments.Add(
                new PowerShellArgument(
                    definition.Name,
                    Format(parameter.SerializedValue, definition),
                    PowerShellArgumentSensitivity.NonSensitive));
        }

        foreach (var definition in version.ParameterDefinitions)
        {
            var supplied = job.Parameters.SingleOrDefault(parameter =>
                string.Equals(
                    parameter.Name,
                    definition.Name,
                    StringComparison.OrdinalIgnoreCase));
            definition.ValidateSerializedValue(supplied?.SerializedValue);
        }

        return arguments;
    }

    private static string Format(
        string value,
        ScriptParameterDefinition definition) =>
        definition.ParameterType switch
        {
            ScriptParameterType.String => value,
            ScriptParameterType.Integer => int.Parse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ScriptParameterType.Boolean => bool.Parse(value)
                .ToString(CultureInfo.InvariantCulture)
                .ToLowerInvariant(),
            ScriptParameterType.DateTime => DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind).ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            ScriptParameterType.Enum => definition.AllowedValues.Single(
                allowed => string.Equals(
                    allowed,
                    value,
                    StringComparison.OrdinalIgnoreCase)),
            _ => throw new AutomationPackageTrustException(
                "The package parameter type is not supported by the reviewed mapper."),
        };
}
