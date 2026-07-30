using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

public sealed class PowerShellTrustTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "WindowsScriptRunner.TrustTests",
        Guid.NewGuid().ToString("N"));
    private readonly string _allowedRoot;
    private readonly string _workingRoot;
    private readonly string _scriptPath;

    public PowerShellTrustTests()
    {
        _allowedRoot = Path.Combine(_root, "allowed");
        _workingRoot = Path.Combine(_root, "working");
        _scriptPath = Path.Combine(_allowedRoot, "ControlledExecutionFixture.ps1");
        Directory.CreateDirectory(_allowedRoot);
        Directory.CreateDirectory(_workingRoot);
        File.Copy(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "ControlledExecutionFixture.ps1"),
            _scriptPath);
    }

    [Fact]
    public void ValidControlledFixturePassesPathAndHashValidation()
    {
        var trusted = PowerShellTestBoundary.CreateTrustedScript(_scriptPath);

        Assert.Equal(
            _scriptPath,
            Validator().ValidateImmediatelyBeforeLaunch(trusted));
    }

    [Fact]
    public void TamperingAfterTrustCreationIsRejected()
    {
        var trusted = PowerShellTestBoundary.CreateTrustedScript(_scriptPath);
        File.AppendAllText(_scriptPath, Environment.NewLine + "# tampered");

        Assert.Throws<PowerShellScriptTrustException>(
            () => Validator().ValidateImmediatelyBeforeLaunch(trusted));
    }

    [Fact]
    public void SiblingPrefixDoesNotEscapeAllowedRoot()
    {
        var sibling = Path.Combine(_root, "allowed-escape");
        Directory.CreateDirectory(sibling);
        var outside = Path.Combine(sibling, "ControlledExecutionFixture.ps1");
        File.Copy(_scriptPath, outside);
        var trusted = PowerShellTestBoundary.CreateTrustedScript(outside);

        Assert.Throws<PowerShellScriptTrustException>(
            () => Validator().ValidateImmediatelyBeforeLaunch(trusted));
    }

    [Fact]
    public void NonCanonicalTraversalPathIsRejected()
    {
        var subdirectory = Path.Combine(_allowedRoot, "sub");
        Directory.CreateDirectory(subdirectory);
        var traversalPath = Path.Combine(
            subdirectory,
            "..",
            Path.GetFileName(_scriptPath));
        var trusted = new TrustedPowerShellScript(
            "ControlledExecutionFixture",
            traversalPath,
            Hash(_scriptPath),
            ["Mode"]);

        Assert.Throws<PowerShellScriptTrustException>(
            () => Validator().ValidateImmediatelyBeforeLaunch(trusted));
    }

    [Theory]
    [InlineData(@"\\server\share\fixture.ps1")]
    [InlineData(@"\\?\C:\fixture.ps1")]
    [InlineData(@"\\.\C:\fixture.ps1")]
    public void UncAndDevicePathsAreRejected(string path)
    {
        var trusted = new TrustedPowerShellScript(
            "ControlledExecutionFixture",
            path,
            new string('A', 64),
            ["Mode"]);

        Assert.Throws<PowerShellScriptTrustException>(
            () => Validator().ValidateImmediatelyBeforeLaunch(trusted));
    }

    [Fact]
    public void AlternateDataStreamSyntaxIsRejected()
    {
        var trusted = new TrustedPowerShellScript(
            "ControlledExecutionFixture",
            _scriptPath + ":alternate",
            Hash(_scriptPath),
            ["Mode"]);

        Assert.Throws<PowerShellScriptTrustException>(
            () => Validator().ValidateImmediatelyBeforeLaunch(trusted));
    }

    [Fact]
    public void JunctionComponentIsRejected()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.Copy(_scriptPath, Path.Combine(outside, Path.GetFileName(_scriptPath)));
        var junction = Path.Combine(_allowedRoot, "junction");
        JunctionTestSupport.Create(junction, outside);
        var link = Path.Combine(junction, Path.GetFileName(_scriptPath));
        try
        {
            var trusted = new TrustedPowerShellScript(
                "ControlledExecutionFixture",
                link,
                Hash(_scriptPath),
                ["Mode"]);

            Assert.Throws<PowerShellScriptTrustException>(
                () => Validator().ValidateImmediatelyBeforeLaunch(trusted));
        }
        finally
        {
            Directory.Delete(junction, recursive: false);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void UnsafeArgumentBindingsAreRejected(PowerShellArgument[] arguments)
    {
        var trusted = PowerShellTestBoundary.CreateTrustedScript(_scriptPath);

        Assert.Throws<PowerShellScriptTrustException>(
            () => new PowerShellArgumentValidator().Validate(trusted, arguments));
    }

    [Fact]
    public async Task FailedHashValidationStillDeletesWorkingDirectory()
    {
        var options = PowerShellIntegrationFixture.CreateOptions(
            _allowedRoot,
            _workingRoot);
        var runtime = new PowerShellRuntimeInfo(
            Path.Combine(_root, "pwsh.exe"),
            new Version(7, 4, 0),
            "Core",
            "Win32NT",
            "Windows",
            "X64",
            false);
        var boundary = PowerShellTestBoundary.Create(
            options,
            runtime,
            _scriptPath,
            NullLogger<PowerShellExecutionBoundary>.Instance);
        var executionId = PowerShellExecutionId.New();
        File.AppendAllText(_scriptPath, Environment.NewLine + "# tampered");

        await Assert.ThrowsAsync<PowerShellScriptTrustException>(
            () => boundary.Boundary.ExecuteAsync(
                boundary.Request(executionId, "Echo"),
                CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(_workingRoot, executionId.ToString())));
    }

    public static TheoryData<PowerShellArgument[]> InvalidArguments
    {
        get
        {
            var data = new TheoryData<PowerShellArgument[]>
            {
                { [new PowerShellArgument("Unknown", "value")] },
                {
                    [
            new PowerShellArgument("Mode", "Echo"),
            new PowerShellArgument("mode", "Echo"),
                    ]
                },
                { [new PowerShellArgument("Message", "contains\0nul")] },
                {
                    [
            new PowerShellArgument(
                "Message",
                new string('X', PowerShellExecutionOptions.MaximumArgumentValueLength + 1)),
                    ]
                },
                {
                    [
            new PowerShellArgument(
                "Message",
                "classified",
                PowerShellArgumentSensitivity.Sensitive),
                    ]
                },
                {
                    [
            .. Enumerable.Range(
                    0,
                    PowerShellExecutionOptions.MaximumArgumentCount + 1)
                .Select(index => new PowerShellArgument($"Name{index}", "value")),
                    ]
                },
            };
            return data;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PowerShellScriptTrustValidator Validator() =>
        new(
            Options.Create(
                PowerShellIntegrationFixture.CreateOptions(
                    _allowedRoot,
                    _workingRoot)));

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
