using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

[CollectionDefinition(Name)]
public sealed class PowerShellIntegrationCollection :
    ICollectionFixture<PowerShellIntegrationFixture>
{
    public const string Name = "PowerShell integration";
}

public sealed class PowerShellIntegrationFixture : IAsyncLifetime
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "WindowsScriptRunner.PowerShellTests",
        Guid.NewGuid().ToString("N"));
    private readonly RecordingLogger<PowerShellExecutionBoundary> _logger = new();
    private PowerShellTestBoundary? _testBoundary;

    public string AllowedRoot => Path.Combine(_testRoot, "allowed");

    public string WorkingRoot => Path.Combine(_testRoot, "working");

    public string ScriptPath => Path.Combine(
        AllowedRoot,
        "ControlledExecutionFixture.ps1");

    public PowerShellRuntimeInfo Runtime => _testBoundary!.Runtime;

    public TrustedPowerShellScript TrustedScript => _testBoundary!.TrustedScript;

    public IPowerShellExecutionBoundary Boundary => _testBoundary!.Boundary;

    public IReadOnlyCollection<string> LogMessages => _logger.Messages;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(AllowedRoot);
        Directory.CreateDirectory(WorkingRoot);
        File.Copy(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "ControlledExecutionFixture.ps1"),
            ScriptPath);
        var options = CreateOptions(AllowedRoot, WorkingRoot);
        var controller = new ProcessTreeController(NullLogger<ProcessTreeController>.Instance);
        var probe = new PowerShellRuntimeProbe(Options.Create(options), controller);
        var locator = new PowerShellExecutableLocator(
            Options.Create(options),
            probe,
            new PowerShellCandidateSource(),
            NullLogger<PowerShellExecutableLocator>.Instance);
        var runtime = await locator.LocateAsync(CancellationToken.None);
        _testBoundary = PowerShellTestBoundary.Create(
            options,
            runtime,
            ScriptPath,
            _logger);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    public PowerShellTestBoundary CreateBoundary(
        Action<PowerShellExecutionOptions> configure) =>
        CreateBoundary(configure, null);

    internal PowerShellTestBoundary CreateBoundary(
        Action<PowerShellExecutionOptions> configure,
        IProcessTreeController? processTreeController)
    {
        var root = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
        var allowedRoot = Path.Combine(root, "allowed");
        var workingRoot = Path.Combine(root, "working");
        Directory.CreateDirectory(allowedRoot);
        Directory.CreateDirectory(workingRoot);
        var scriptPath = Path.Combine(allowedRoot, "ControlledExecutionFixture.ps1");
        File.Copy(ScriptPath, scriptPath);
        var options = CreateOptions(allowedRoot, workingRoot);
        configure(options);
        return PowerShellTestBoundary.Create(
            options,
            Runtime,
            scriptPath,
            new RecordingLogger<PowerShellExecutionBoundary>(),
            processTreeController);
    }

    internal static PowerShellExecutionOptions CreateOptions(
        string allowedRoot,
        string workingRoot) =>
        new()
        {
            AllowedScriptRoot = allowedRoot,
            WorkingRoot = workingRoot,
            MinimumVersion = "7.4.0",
            ProbeTimeoutSeconds = 10,
            DefaultTimeoutSeconds = 10,
            MaximumTimeoutSeconds = 30,
            TerminationGraceSeconds = 5,
        };
}

public sealed class PowerShellTestBoundary
{
    private static readonly string[] AllowedParameters =
    [
        "Mode",
        "Message",
        "RequestedExitCode",
        "SleepSeconds",
        "EnvironmentVariableName",
    ];

    private PowerShellTestBoundary(
        PowerShellExecutionOptions options,
        PowerShellRuntimeInfo runtime,
        string scriptPath,
        TrustedPowerShellScript trustedScript,
        IPowerShellExecutionBoundary boundary)
    {
        Options = options;
        Runtime = runtime;
        ScriptPath = scriptPath;
        TrustedScript = trustedScript;
        Boundary = boundary;
    }

    public PowerShellExecutionOptions Options { get; }

    public PowerShellRuntimeInfo Runtime { get; }

    public string ScriptPath { get; }

    public TrustedPowerShellScript TrustedScript { get; }

    public IPowerShellExecutionBoundary Boundary { get; }

    internal static PowerShellTestBoundary Create(
        PowerShellExecutionOptions options,
        PowerShellRuntimeInfo runtime,
        string scriptPath,
        ILogger<PowerShellExecutionBoundary> boundaryLogger,
        IProcessTreeController? processTreeController = null)
    {
        var wrappedOptions =
            Microsoft.Extensions.Options.Options.Create(options);
        var controller = processTreeController ??
            new ProcessTreeController(NullLogger<ProcessTreeController>.Instance);
        var trusted = CreateTrustedScript(scriptPath);
        var boundary = new PowerShellExecutionBoundary(
            wrappedOptions,
            new FixedRuntimeLocator(runtime),
            new PowerShellScriptTrustValidator(wrappedOptions),
            new PowerShellArgumentValidator(),
            new ExecutionWorkingDirectory(
                wrappedOptions,
                NullLogger<ExecutionWorkingDirectory>.Instance),
            controller,
            boundaryLogger);
        return new PowerShellTestBoundary(options, runtime, scriptPath, trusted, boundary);
    }

    public static TrustedPowerShellScript CreateTrustedScript(string scriptPath) =>
        new(
            "ControlledExecutionFixture",
            Path.GetFullPath(scriptPath),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(scriptPath))),
            AllowedParameters);

    public PowerShellExecutionRequest Request(
        PowerShellExecutionId executionId,
        string mode,
        TimeSpan? timeout = null,
        params PowerShellArgument[] arguments) =>
        new(
            executionId,
            TrustedScript,
            [new PowerShellArgument("Mode", mode), .. arguments],
            timeout);
}

internal sealed class FixedRuntimeLocator(PowerShellRuntimeInfo runtime) :
    IPowerShellExecutableLocator
{
    public Task<PowerShellRuntimeInfo> LocateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(runtime);
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _messages.Enqueue(formatter(state, exception));
}

internal sealed class FailingAfterKillProcessTreeController : IProcessTreeController
{
    public int? TerminatedProcessId { get; private set; }

    public ProcessTreeContainment Attach(Process process) =>
        new(null);

    public async Task TerminateAsync(
        Process process,
        ProcessTreeContainment containment,
        TimeSpan gracePeriod,
        PowerShellExecutionId? executionId)
    {
        TerminatedProcessId = process.Id;
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync();
        throw new PowerShellProcessTerminationException(
            "Injected process-tree termination failure.");
    }
}

internal sealed class FallbackProcessTreeController : IProcessTreeController
{
    private readonly ProcessTreeController _controller =
        new(NullLogger<ProcessTreeController>.Instance);

    public ProcessTreeContainment Attach(Process process) => new(null);

    public Task TerminateAsync(
        Process process,
        ProcessTreeContainment containment,
        TimeSpan gracePeriod,
        PowerShellExecutionId? executionId) =>
        _controller.TerminateAsync(process, containment, gracePeriod, executionId);
}

internal static class ProcessTest
{
    public static int ParseProcessId(string output, string key)
    {
        var line = output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Single(value => value.StartsWith(key + "=", StringComparison.Ordinal));
        return int.Parse(line[(key.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<string> WaitForFileAsync(string path)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var content = (await File.ReadAllTextAsync(path)).Trim();
                if (content.Length > 0)
                {
                    return content;
                }
            }
            catch (IOException)
            {
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Expected non-empty fixture marker '{path}'.");
        return string.Empty;
    }

    public static async Task AssertExitedAsync(int processId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Process {processId} remained alive.");
    }
}
