using System.Reflection;

namespace WindowsScriptRunner.UnitTests;

public sealed class ArchitectureTests
{
    private static readonly string[] SourceAssemblyNames =
    [
        "WindowsScriptRunner.Application",
        "WindowsScriptRunner.Contracts",
        "WindowsScriptRunner.Domain",
        "WindowsScriptRunner.Infrastructure",
        "WindowsScriptRunner.PowerShell",
        "WindowsScriptRunner.Reporting",
        "WindowsScriptRunner.Web",
        "WindowsScriptRunner.Worker",
    ];

    [Fact]
    public void ExpectedSourceAssembliesCanBeLoaded()
    {
        foreach (var assemblyName in SourceAssemblyNames)
        {
            var assembly = Assembly.Load(assemblyName);
            Assert.Equal(assemblyName, assembly.GetName().Name);
        }
    }

    [Fact]
    public void DomainDoesNotReferenceOuterLayers()
    {
        string[] forbiddenReferences =
        [
            "WindowsScriptRunner.Infrastructure",
            "WindowsScriptRunner.PowerShell",
            "WindowsScriptRunner.Reporting",
            "WindowsScriptRunner.Web",
            "WindowsScriptRunner.Worker",
        ];

        var references = typeof(Domain.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain(references, forbiddenReferences.Contains);
    }
}
