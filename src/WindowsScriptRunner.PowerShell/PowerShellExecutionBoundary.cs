using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.PowerShell;

internal sealed class PowerShellExecutionBoundary(
    IOptions<PowerShellExecutionOptions> options,
    IPowerShellExecutableLocator executableLocator,
    IPowerShellScriptTrustValidator scriptTrustValidator,
    IPowerShellArgumentValidator argumentValidator,
    IExecutionWorkingDirectory workingDirectory,
    IProcessTreeController processTreeController,
    ILogger<PowerShellExecutionBoundary> logger) : IPowerShellExecutionBoundary
{
    private readonly PowerShellExecutionOptions _options = options.Value;

    public async Task<PowerShellExecutionResult> ExecuteAsync(
        PowerShellExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        argumentValidator.Validate(request.Script, request.Arguments);
        var timeout = ResolveTimeout(request.Timeout);
        var runtime = await executableLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var executionWorkingDirectory = workingDirectory.Create(request.ExecutionId);

        try
        {
            var scriptPath =
                scriptTrustValidator.ValidateImmediatelyBeforeLaunch(request.Script);
            return await ExecuteProcessAsync(
                    request,
                    runtime,
                    scriptPath,
                    executionWorkingDirectory,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await workingDirectory.DeleteAsync(executionWorkingDirectory).ConfigureAwait(false);
        }
    }

    private async Task<PowerShellExecutionResult> ExecuteProcessAsync(
        PowerShellExecutionRequest request,
        PowerShellRuntimeInfo runtime,
        string scriptPath,
        string executionWorkingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(
            request,
            runtime,
            scriptPath,
            executionWorkingDirectory);
        using var process = new Process { StartInfo = startInfo };
        var startedUtc = DateTimeOffset.UtcNow;
        try
        {
            if (!process.Start())
            {
                throw new PowerShellProcessStartException(
                    "The PowerShell child process did not start.",
                    new InvalidOperationException("Process.Start returned false."));
            }
        }
        catch (PowerShellExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new PowerShellProcessStartException(
                "The PowerShell child process could not start.",
                exception);
        }

        using var containment = processTreeController.Attach(process);
        logger.LogInformation(
            "PowerShell execution {ExecutionId} started for {ArtifactName} with runtime {PowerShellVersion}.",
            request.ExecutionId,
            request.Script.ArtifactName,
            runtime.Version);

        using var capture = new BoundedProcessOutput(
            _options.MaximumStandardOutputBytes,
            _options.MaximumStandardErrorBytes,
            _options.MaximumCombinedOutputBytes);
        var standardOutputTask =
            capture.PumpStandardOutputAsync(process.StandardOutput.BaseStream);
        var standardErrorTask =
            capture.PumpStandardErrorAsync(process.StandardError.BaseStream);
        var outputPumpsTask = Task.WhenAll(standardOutputTask, standardErrorTask);
        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        using var timeoutCancellation = new CancellationTokenSource();
        var timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
        var cancellationSignal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellationSignal);
        var completed = await PowerShellProcessLifecycle.WaitAsync(
                exitTask,
                outputPumpsTask,
                timeoutTask,
                capture.LimitExceeded,
                cancellationSignal.Task)
            .ConfigureAwait(false);
        timeoutCancellation.Cancel();

        var callerCancelled =
            completed == cancellationSignal.Task && cancellationToken.IsCancellationRequested;
        PowerShellTerminationReason terminationReason;
        int? exitCode;
        if (completed == outputPumpsTask && !capture.LimitExceeded.IsCompleted)
        {
            terminationReason = PowerShellTerminationReason.Exited;
            exitCode = process.ExitCode;
            if (containment.UsesFallback)
            {
                await processTreeController.TerminateAsync(
                        process,
                        containment,
                        TimeSpan.FromSeconds(_options.TerminationGraceSeconds),
                        request.ExecutionId)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            if (callerCancelled)
            {
                capture.StopStoring();
            }

            terminationReason = capture.LimitExceeded.IsCompleted
                ? PowerShellTerminationReason.OutputLimitExceeded
                : PowerShellTerminationReason.TimedOut;
            exitCode = null;
            try
            {
                await processTreeController.TerminateAsync(
                        process,
                        containment,
                        TimeSpan.FromSeconds(_options.TerminationGraceSeconds),
                        request.ExecutionId)
                    .ConfigureAwait(false);
            }
            catch (Exception terminationException)
            {
                var pumpException = await CloseAndObservePumpsAsync(
                        process,
                        standardOutputTask,
                        standardErrorTask)
                    .ConfigureAwait(false);
                if (pumpException is not null)
                {
                    terminationException.Data["OutputPumpException"] = pumpException;
                }

                throw;
            }
        }

        await outputPumpsTask.ConfigureAwait(false);
        if (terminationReason == PowerShellTerminationReason.Exited &&
            capture.LimitExceeded.IsCompleted)
        {
            terminationReason = PowerShellTerminationReason.OutputLimitExceeded;
            exitCode = null;
        }

        if (callerCancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var completedUtc = DateTimeOffset.UtcNow;
        var output = capture.Snapshot();
        logger.LogInformation(
            "PowerShell execution {ExecutionId} completed for {ArtifactName} in {DurationMs} ms " +
            "with exit code {ExitCode}, reason {TerminationReason}, stdout bytes " +
            "{StandardOutputBytes}, stderr bytes {StandardErrorBytes}, stdout truncated " +
            "{StandardOutputTruncated}, and stderr truncated {StandardErrorTruncated}.",
            request.ExecutionId,
            request.Script.ArtifactName,
            (completedUtc - startedUtc).TotalMilliseconds,
            exitCode,
            terminationReason,
            output.StandardOutputBytes,
            output.StandardErrorBytes,
            output.StandardOutputTruncated,
            output.StandardErrorTruncated);
        return new PowerShellExecutionResult(
            request.ExecutionId,
            runtime,
            startedUtc,
            completedUtc,
            completedUtc - startedUtc,
            exitCode,
            output.StandardOutput,
            output.StandardError,
            output.StandardOutputBytes,
            output.StandardErrorBytes,
            output.StandardOutputTruncated,
            output.StandardErrorTruncated,
            terminationReason);
    }

    private static ProcessStartInfo CreateStartInfo(
        PowerShellExecutionRequest request,
        PowerShellRuntimeInfo runtime,
        string scriptPath,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add($"-{argument.Name}");
            startInfo.ArgumentList.Add(argument.Value);
        }

        PowerShellEnvironment.Apply(startInfo);
        return startInfo;
    }

    private TimeSpan ResolveTimeout(TimeSpan? requestedTimeout)
    {
        var timeout = requestedTimeout ??
            TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds);
        if (timeout <= TimeSpan.Zero ||
            timeout == Timeout.InfiniteTimeSpan ||
            timeout > TimeSpan.FromSeconds(_options.MaximumTimeoutSeconds))
        {
            throw new PowerShellExecutionException(
                "The PowerShell execution timeout is outside the configured bounds.");
        }

        return timeout;
    }

    private static async Task<Exception?> CloseAndObservePumpsAsync(
        Process process,
        Task standardOutputTask,
        Task standardErrorTask)
    {
        process.StandardOutput.Dispose();
        process.StandardError.Dispose();
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
