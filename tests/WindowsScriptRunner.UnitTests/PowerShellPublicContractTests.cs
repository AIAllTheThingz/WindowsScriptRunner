using System.Reflection;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.UnitTests;

public sealed class PowerShellPublicContractTests
{
    [Fact]
    public void PowerShellExecutionIdentifierRejectsEmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => new PowerShellExecutionId(Guid.Empty));
        Assert.NotEqual(PowerShellExecutionId.New(), PowerShellExecutionId.New());
    }

    [Fact]
    public void PowerShellBoundaryAcceptsNoRawScriptOrCommandString()
    {
        var execute = Assert.Single(typeof(IPowerShellExecutionBoundary).GetMethods());

        Assert.Equal(
            typeof(PowerShellExecutionRequest),
            execute.GetParameters()[0].ParameterType);
        Assert.DoesNotContain(
            execute.GetParameters(),
            parameter => parameter.ParameterType == typeof(string));
    }

    [Fact]
    public void TrustedScriptHasNoPublicConstructor()
    {
        Assert.Empty(typeof(TrustedPowerShellScript).GetConstructors());
        Assert.NotEmpty(
            typeof(TrustedPowerShellScript).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void PowerShellOptionsRejectMissingTrustAndWorkingRoots()
    {
        var result = new PowerShellExecutionOptionsValidator()
            .Validate(null, new PowerShellExecutionOptions());

        Assert.False(result.Succeeded);
    }
}
