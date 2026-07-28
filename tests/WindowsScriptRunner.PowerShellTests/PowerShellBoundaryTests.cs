using System.Reflection;

namespace WindowsScriptRunner.PowerShellTests;

public sealed class PowerShellBoundaryTests
{
    [Fact]
    public void InitialAssemblyLoadsSuccessfully()
    {
        var assembly = typeof(PowerShell.AssemblyMarker).Assembly;

        Assert.Equal("WindowsScriptRunner.PowerShell", assembly.GetName().Name);
    }

    [Fact]
    public void AssemblyDoesNotExposeArbitraryCommandExecutionApi()
    {
        string[] prohibitedTerms = ["Command", "Execute", "Invoke", "ScriptText"];
        var exposedMethods = typeof(PowerShell.AssemblyMarker).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.DeclaringType?.Assembly == typeof(PowerShell.AssemblyMarker).Assembly);

        Assert.DoesNotContain(
            exposedMethods,
            method => prohibitedTerms.Any(
                term => method.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }
}
