using System.Reflection;

namespace WindowsScriptRunner.SecurityTests;

public sealed class ProjectBoundaryTests
{
    public static TheoryData<string, string> ForbiddenReferences => new()
    {
        { "WindowsScriptRunner.Web", "WindowsScriptRunner.PowerShell" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Infrastructure" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Web" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Worker" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.PowerShell" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Reporting" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void AssemblyDoesNotDirectlyReferenceForbiddenProject(string assemblyName, string forbiddenName)
    {
        var references = Assembly.Load(assemblyName).GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name == forbiddenName);
    }
}
