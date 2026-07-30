using System.Diagnostics;

namespace WindowsScriptRunner.PowerShell;

internal static class PowerShellEnvironment
{
    private static readonly string[] InheritedVariableNames =
    [
        "SystemRoot",
        "WINDIR",
        "TEMP",
        "TMP",
        "PATH",
        "ComSpec",
        "PATHEXT",
        "ProgramFiles",
        "ProgramFiles(x86)",
        "ProgramData",
        "LOCALAPPDATA",
        "APPDATA",
        "USERPROFILE",
    ];

    public static void Apply(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment.Clear();
        foreach (var name in InheritedVariableNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                startInfo.Environment[name] = value;
            }
        }

        startInfo.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["POWERSHELL_UPDATECHECK"] = "Off";
    }
}
