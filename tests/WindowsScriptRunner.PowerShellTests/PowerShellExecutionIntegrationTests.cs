using System.Text;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

[Collection(PowerShellIntegrationCollection.Name)]
public sealed class PowerShellExecutionIntegrationTests(
    PowerShellIntegrationFixture fixture)
{
    public static TheoryData<string> LiteralMessages => new()
    {
        "plain",
        "text with spaces",
        "\"double quotes\"",
        "'single quotes'",
        "semicolon; Write-Output never",
        "ampersand & value",
        "pipe | value",
        "backtick ` value",
        "dollar $(Write-Output never) and ${HOME}",
        "wildcards * ? and redirection > <",
        "Unicode Ελληνικά 日本語 😀",
        "line one\nline two",
        "",
        "\"; Write-Output WSR_INJECTION_SUCCEEDED; #",
    };

    [Fact]
    public void InstalledRuntimeWasActuallyLocatedAndProbed()
    {
        Assert.True(Path.IsPathFullyQualified(fixture.Runtime.ExecutablePath));
        Assert.True(File.Exists(fixture.Runtime.ExecutablePath));
        Assert.True(fixture.Runtime.Version >= new Version(7, 4, 0));
        Assert.Equal("Core", fixture.Runtime.PSEdition);
        Assert.Equal("Win32NT", fixture.Runtime.Platform);
        Assert.Contains(fixture.Runtime.ProcessArchitecture, new[] { "X64", "Arm64" });
        Assert.False(fixture.Runtime.IsPreview);
    }

    [Theory]
    [MemberData(nameof(LiteralMessages))]
    public async Task ArgumentListPreservesExactLiteralMessage(string message)
    {
        var result = await fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                PowerShellExecutionId.New(),
                fixture.TrustedScript,
                [
                    new PowerShellArgument("Mode", "Echo"),
                    new PowerShellArgument("Message", message),
                ],
                TimeSpan.FromSeconds(10)),
            CancellationToken.None);

        Assert.Equal(PowerShellTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(message, DecodeValue(result.StandardOutput, "ECHO_BASE64"));
        Assert.DoesNotContain(
            "WSR_INJECTION_SUCCEEDED",
            result.StandardOutput,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(37)]
    public async Task ExitCodeIsReturnedExactlyWithoutThrowing(int requestedExitCode)
    {
        var result = await fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                PowerShellExecutionId.New(),
                fixture.TrustedScript,
                [
                    new PowerShellArgument("Mode", "ExitCode"),
                    new PowerShellArgument(
                        "RequestedExitCode",
                        requestedExitCode.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)),
                ],
                TimeSpan.FromSeconds(10)),
            CancellationToken.None);

        Assert.Equal(PowerShellTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(requestedExitCode, result.ExitCode);
        Assert.Contains(
            $"WSR_EXIT_CODE={requestedExitCode}",
            result.StandardOutput,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task StandardStreamsAreCapturedConcurrentlyForAnyExitCode(int exitCode)
    {
        var result = await fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                PowerShellExecutionId.New(),
                fixture.TrustedScript,
                [
                    new PowerShellArgument("Mode", "Streams"),
                    new PowerShellArgument(
                        "RequestedExitCode",
                        exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ],
                TimeSpan.FromSeconds(10)),
            CancellationToken.None);

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Contains("WSR_STDOUT_0_✓", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("WSR_STDOUT_511_✓", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("WSR_STDERR_0_✓", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("WSR_STDERR_511_✓", result.StandardError, StringComparison.Ordinal);
        Assert.Equal(
            Encoding.UTF8.GetByteCount(result.StandardOutput),
            result.StandardOutputBytes);
        Assert.Equal(
            Encoding.UTF8.GetByteCount(result.StandardError),
            result.StandardErrorBytes);
        Assert.False(result.StandardOutputTruncated);
        Assert.False(result.StandardErrorTruncated);
    }

    [Fact]
    public async Task ParentSecretsAreAbsentButRequiredWindowsVariablesRemain()
    {
        string[] names =
        [
            "WSR_PRIVATE_SENTINEL",
            "OPENAI_API_KEY",
            "ConnectionStrings__WindowsScriptRunner",
        ];
        try
        {
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, "harmless-parent-sentinel");
                var result = await fixture.Boundary.ExecuteAsync(
                    new PowerShellExecutionRequest(
                        PowerShellExecutionId.New(),
                        fixture.TrustedScript,
                        [
                            new PowerShellArgument("Mode", "Environment"),
                            new PowerShellArgument("EnvironmentVariableName", name),
                        ],
                        TimeSpan.FromSeconds(10)),
                    CancellationToken.None);

                Assert.Contains(
                    "ENVIRONMENT_PRESENT=False",
                    result.StandardOutput,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "SYSTEMROOT_PRESENT=True",
                    result.StandardOutput,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "TEMP_PRESENT=True",
                    result.StandardOutput,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "harmless-parent-sentinel",
                    result.StandardOutput,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    [Fact]
    public async Task WorkingDirectoryIsUniqueIsolatedAndDeletedAfterExit()
    {
        var firstId = PowerShellExecutionId.New();
        var secondId = PowerShellExecutionId.New();
        var first = await fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                firstId,
                fixture.TrustedScript,
                [new PowerShellArgument("Mode", "WorkingDirectory")],
                TimeSpan.FromSeconds(10)),
            CancellationToken.None);
        var second = await fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                secondId,
                fixture.TrustedScript,
                [new PowerShellArgument("Mode", "WorkingDirectory")],
                TimeSpan.FromSeconds(10)),
            CancellationToken.None);
        var firstPath = DecodeValue(first.StandardOutput, "WORKING_DIRECTORY_BASE64");
        var secondPath = DecodeValue(second.StandardOutput, "WORKING_DIRECTORY_BASE64");

        Assert.Equal(Path.Combine(fixture.WorkingRoot, firstId.ToString()), firstPath);
        Assert.Equal(Path.Combine(fixture.WorkingRoot, secondId.ToString()), secondPath);
        Assert.NotEqual(firstPath, secondPath);
        Assert.NotEqual(
            Path.GetDirectoryName(fixture.ScriptPath),
            firstPath);
        Assert.False(Directory.Exists(firstPath));
        Assert.False(Directory.Exists(secondPath));
    }

    [Fact]
    public async Task TimeoutReturnsDistinctResultAndTerminatesRootProcess()
    {
        var executionId = PowerShellExecutionId.New();
        var result = await fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                executionId,
                fixture.TrustedScript,
                [
                    new PowerShellArgument("Mode", "Sleep"),
                    new PowerShellArgument("SleepSeconds", "10"),
                ],
                TimeSpan.FromSeconds(1)),
            CancellationToken.None);
        var processId = ProcessTest.ParseProcessId(result.StandardOutput, "PARENT_PID");

        Assert.Equal(PowerShellTerminationReason.TimedOut, result.TerminationReason);
        Assert.Null(result.ExitCode);
        Assert.InRange(result.Duration, TimeSpan.Zero, TimeSpan.FromSeconds(6));
        Assert.False(
            Directory.Exists(Path.Combine(fixture.WorkingRoot, executionId.ToString())));
        await ProcessTest.AssertExitedAsync(processId);
    }

    [Fact]
    public async Task InFlightCancellationThrowsAndCleansUp()
    {
        var executionId = PowerShellExecutionId.New();
        var executionPath = Path.Combine(fixture.WorkingRoot, executionId.ToString());
        using var cancellation = new CancellationTokenSource();
        var executionTask = fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                executionId,
                fixture.TrustedScript,
                [
                    new PowerShellArgument("Mode", "SpawnChild"),
                    new PowerShellArgument("SleepSeconds", "10"),
                ],
                TimeSpan.FromSeconds(20)),
            cancellation.Token);
        var marker = Path.Combine(executionPath, "started.marker");
        var childMarker = Path.Combine(executionPath, "child.marker");
        var processId = int.Parse(
            await ProcessTest.WaitForFileAsync(marker),
            System.Globalization.CultureInfo.InvariantCulture);
        var childProcessId = int.Parse(
            await ProcessTest.WaitForFileAsync(childMarker),
            System.Globalization.CultureInfo.InvariantCulture);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executionTask);
        Assert.False(Directory.Exists(executionPath));
        await ProcessTest.AssertExitedAsync(processId);
        await ProcessTest.AssertExitedAsync(childProcessId);
    }

    [Fact]
    public async Task TimeoutTerminatesSpawnedChildProcessTree()
    {
        var executionId = PowerShellExecutionId.New();
        var result = await fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                executionId,
                fixture.TrustedScript,
                [
                    new PowerShellArgument("Mode", "SpawnChild"),
                    new PowerShellArgument("SleepSeconds", "10"),
                ],
                TimeSpan.FromSeconds(1)),
            CancellationToken.None);
        var parentId = ProcessTest.ParseProcessId(result.StandardOutput, "PARENT_PID");
        var childId = ProcessTest.ParseProcessId(result.StandardOutput, "CHILD_PID");

        Assert.Equal(PowerShellTerminationReason.TimedOut, result.TerminationReason);
        Assert.Null(result.ExitCode);
        Assert.False(
            Directory.Exists(Path.Combine(fixture.WorkingRoot, executionId.ToString())));
        await ProcessTest.AssertExitedAsync(parentId);
        await ProcessTest.AssertExitedAsync(childId);
    }

    [Fact]
    public async Task TimeoutRemainsActiveAfterRootExitsWhileChildHoldsOutputPipes()
    {
        var executionId = PowerShellExecutionId.New();
        var result = await fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                executionId,
                fixture.TrustedScript,
                [
                    new PowerShellArgument("Mode", "SpawnChild"),
                    new PowerShellArgument("SleepSeconds", "0"),
                ],
                TimeSpan.FromSeconds(1)),
            CancellationToken.None);
        var parentId = ProcessTest.ParseProcessId(result.StandardOutput, "PARENT_PID");
        var childId = ProcessTest.ParseProcessId(result.StandardOutput, "CHILD_PID");

        Assert.Equal(PowerShellTerminationReason.TimedOut, result.TerminationReason);
        Assert.Null(result.ExitCode);
        Assert.InRange(result.Duration, TimeSpan.Zero, TimeSpan.FromSeconds(6));
        Assert.False(
            Directory.Exists(Path.Combine(fixture.WorkingRoot, executionId.ToString())));
        await ProcessTest.AssertExitedAsync(parentId);
        await ProcessTest.AssertExitedAsync(childId);
    }

    [Fact]
    public async Task FallbackTerminatesDescendantAfterRootExits()
    {
        var boundary = fixture.CreateBoundary(
            _ => { },
            new FallbackProcessTreeController());
        var executionId = PowerShellExecutionId.New();
        var result = await boundary.Boundary.ExecuteAsync(
            boundary.Request(
                executionId,
                "SpawnChild",
                TimeSpan.FromSeconds(1),
                new PowerShellArgument("SleepSeconds", "0")),
            CancellationToken.None);
        var parentId = ProcessTest.ParseProcessId(result.StandardOutput, "PARENT_PID");
        var childId = ProcessTest.ParseProcessId(result.StandardOutput, "CHILD_PID");

        Assert.Equal(PowerShellTerminationReason.TimedOut, result.TerminationReason);
        Assert.Null(result.ExitCode);
        Assert.InRange(result.Duration, TimeSpan.Zero, TimeSpan.FromSeconds(6));
        Assert.False(
            Directory.Exists(
                Path.Combine(boundary.Options.WorkingRoot!, executionId.ToString())));
        await ProcessTest.AssertExitedAsync(parentId);
        await ProcessTest.AssertExitedAsync(childId);
    }

    [Fact]
    public async Task FallbackTerminatesDetachedDescendantAfterNormalRootExit()
    {
        var boundary = fixture.CreateBoundary(
            _ => { },
            new FallbackProcessTreeController());
        var executionId = PowerShellExecutionId.New();
        var result = await boundary.Boundary.ExecuteAsync(
            boundary.Request(
                executionId,
                "SpawnChild",
                TimeSpan.FromSeconds(10),
                new PowerShellArgument("Message", "DetachedOutput"),
                new PowerShellArgument("SleepSeconds", "0")),
            CancellationToken.None);
        var parentId = ProcessTest.ParseProcessId(result.StandardOutput, "PARENT_PID");
        var childId = ProcessTest.ParseProcessId(result.StandardOutput, "CHILD_PID");

        Assert.Equal(PowerShellTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(0, result.ExitCode);
        await ProcessTest.AssertExitedAsync(parentId);
        await ProcessTest.AssertExitedAsync(childId);
    }

    [Fact]
    public async Task FallbackNormalExitWithoutDescendantsRemainsSuccessful()
    {
        var boundary = fixture.CreateBoundary(
            _ => { },
            new FallbackProcessTreeController());

        var result = await boundary.Boundary.ExecuteAsync(
            boundary.Request(PowerShellExecutionId.New(), "Echo"),
            CancellationToken.None);

        Assert.Equal(PowerShellTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ShortLivedOutputBeyondLimitIsClassifiedAsExceeded()
    {
        var boundary = fixture.CreateBoundary(
            options =>
            {
                options.MaximumStandardOutputBytes = 1;
                options.MaximumStandardErrorBytes = 1024;
                options.MaximumCombinedOutputBytes = 1024;
            });

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var executionId = PowerShellExecutionId.New();
            var result = await boundary.Boundary.ExecuteAsync(
                boundary.Request(
                    executionId,
                    "ExitCode",
                    TimeSpan.FromSeconds(10)),
                CancellationToken.None);

            Assert.Equal(
                PowerShellTerminationReason.OutputLimitExceeded,
                result.TerminationReason);
            Assert.Null(result.ExitCode);
            Assert.True(result.StandardOutputTruncated);
            Assert.False(
                Directory.Exists(
                    Path.Combine(boundary.Options.WorkingRoot!, executionId.ToString())));
        }
    }

    [Fact]
    public async Task TerminationFailureIsPreservedAfterOutputPumpsAreObserved()
    {
        var controller = new FailingAfterKillProcessTreeController();
        var boundary = fixture.CreateBoundary(_ => { }, controller);
        var executionId = PowerShellExecutionId.New();

        await Assert.ThrowsAsync<PowerShellProcessTerminationException>(
            () => boundary.Boundary.ExecuteAsync(
                boundary.Request(
                    executionId,
                    "Sleep",
                    TimeSpan.FromSeconds(1),
                    new PowerShellArgument("SleepSeconds", "10")),
                CancellationToken.None));

        Assert.NotNull(controller.TerminatedProcessId);
        Assert.False(
            Directory.Exists(
                Path.Combine(boundary.Options.WorkingRoot!, executionId.ToString())));
        await ProcessTest.AssertExitedAsync(controller.TerminatedProcessId.Value);
    }

    [Theory]
    [InlineData("StdOut")]
    [InlineData("StdErr")]
    [InlineData("Both")]
    public async Task OutputLimitsTerminateAndBoundCapturedContent(string streamMode)
    {
        var boundary = fixture.CreateBoundary(
            options =>
            {
                options.MaximumStandardOutputBytes = streamMode == "StdOut" ? 1024 : 4096;
                options.MaximumStandardErrorBytes = streamMode == "StdErr" ? 1024 : 4096;
                options.MaximumCombinedOutputBytes = 4096;
            });
        var executionId = PowerShellExecutionId.New();
        var result = await boundary.Boundary.ExecuteAsync(
            boundary.Request(
                executionId,
                "FloodOutput",
                TimeSpan.FromSeconds(10),
                new PowerShellArgument("Message", streamMode)),
            CancellationToken.None);
        var processId = ProcessTest.ParseProcessId(result.StandardOutput, "PARENT_PID");

        Assert.Equal(
            PowerShellTerminationReason.OutputLimitExceeded,
            result.TerminationReason);
        Assert.Null(result.ExitCode);
        Assert.True(
            result.StandardOutputTruncated || result.StandardErrorTruncated);
        Assert.True(
            Encoding.UTF8.GetByteCount(result.StandardOutput) <=
            boundary.Options.MaximumStandardOutputBytes);
        Assert.True(
            Encoding.UTF8.GetByteCount(result.StandardError) <=
            boundary.Options.MaximumStandardErrorBytes);
        Assert.True(
            Encoding.UTF8.GetByteCount(result.StandardOutput) +
            Encoding.UTF8.GetByteCount(result.StandardError) <=
            boundary.Options.MaximumCombinedOutputBytes);
        Assert.False(
            Directory.Exists(Path.Combine(boundary.Options.WorkingRoot!, executionId.ToString())));
        await ProcessTest.AssertExitedAsync(processId);
    }

    [Fact]
    public async Task LogsContainOnlySafeStructuredMetadata()
    {
        var executionId = PowerShellExecutionId.New();
        const string parameterValue = "WSR_PARAMETER_MUST_NOT_BE_LOGGED";
        var result = await fixture.Boundary.ExecuteAsync(
            new PowerShellExecutionRequest(
                executionId,
                fixture.TrustedScript,
                [
                    new PowerShellArgument("Mode", "Echo"),
                    new PowerShellArgument("Message", parameterValue),
                ],
                TimeSpan.FromSeconds(10)),
            CancellationToken.None);
        var logs = string.Join(Environment.NewLine, fixture.LogMessages);

        Assert.Contains(executionId.ToString(), logs, StringComparison.Ordinal);
        Assert.Contains("ControlledExecutionFixture", logs, StringComparison.Ordinal);
        Assert.Contains(fixture.Runtime.Version.ToString(), logs, StringComparison.Ordinal);
        Assert.Contains("Exited", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(parameterValue, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(result.StandardOutput, logs, StringComparison.Ordinal);
        if (result.StandardError.Length > 0)
        {
            Assert.DoesNotContain(result.StandardError, logs, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(fixture.ScriptPath, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings__", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnallowedOrDuplicateParametersFailBeforeWorkingDirectoryCreation()
    {
        var unknownId = PowerShellExecutionId.New();
        await Assert.ThrowsAsync<PowerShellScriptTrustException>(
            () => fixture.Boundary.ExecuteAsync(
                new PowerShellExecutionRequest(
                    unknownId,
                    fixture.TrustedScript,
                    [new PowerShellArgument("Unknown", "value")],
                    TimeSpan.FromSeconds(10)),
                CancellationToken.None));
        Assert.False(
            Directory.Exists(Path.Combine(fixture.WorkingRoot, unknownId.ToString())));

        var duplicateId = PowerShellExecutionId.New();
        await Assert.ThrowsAsync<PowerShellScriptTrustException>(
            () => fixture.Boundary.ExecuteAsync(
                new PowerShellExecutionRequest(
                    duplicateId,
                    fixture.TrustedScript,
                    [
                        new PowerShellArgument("Mode", "Echo"),
                        new PowerShellArgument("mode", "Echo"),
                    ],
                    TimeSpan.FromSeconds(10)),
                CancellationToken.None));
        Assert.False(
            Directory.Exists(Path.Combine(fixture.WorkingRoot, duplicateId.ToString())));
    }

    private static string DecodeValue(string output, string key)
    {
        var prefix = key + "=";
        var line = output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return Encoding.UTF8.GetString(Convert.FromBase64String(line[prefix.Length..]));
    }
}
