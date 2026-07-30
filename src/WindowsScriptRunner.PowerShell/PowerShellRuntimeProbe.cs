using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace WindowsScriptRunner.PowerShell;

internal interface IPowerShellRuntimeProbe
{
    Task<PowerShellRuntimeInfo> ProbeAsync(
        string executablePath,
        CancellationToken cancellationToken);
}

internal sealed class PowerShellRuntimeProbe(
    IOptions<PowerShellExecutionOptions> options,
    IProcessTreeController processTreeController) : IPowerShellRuntimeProbe
{
    internal const string ProbeCommand =
        "[ordered]@{Version=$PSVersionTable.PSVersion.ToString();" +
        "PSEdition=$PSVersionTable.PSEdition;Platform=$PSVersionTable.Platform;" +
        "OS=$PSVersionTable.OS;Architecture=" +
        "[System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()}" +
        "|ConvertTo-Json -Compress";
    private const int ProbeStreamLimit = 65_536;
    private readonly PowerShellExecutionOptions _options = options.Value;

    public async Task<PowerShellRuntimeInfo> ProbeAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateExecutablePath(executablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(ProbeCommand);
        PowerShellEnvironment.Apply(startInfo);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new PowerShellRuntimeValidationException(
                    "The PowerShell runtime probe did not start.");
            }
        }
        catch (PowerShellExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new PowerShellRuntimeValidationException(
                "The PowerShell runtime probe could not start.",
                exception);
        }

        using var containment = processTreeController.Attach(process);
        using var capture = new BoundedProcessOutput(
            ProbeStreamLimit,
            ProbeStreamLimit,
            ProbeStreamLimit * 2);
        var standardOutputTask =
            capture.PumpStandardOutputAsync(process.StandardOutput.BaseStream);
        var standardErrorTask =
            capture.PumpStandardErrorAsync(process.StandardError.BaseStream);
        var outputPumpsTask = Task.WhenAll(standardOutputTask, standardErrorTask);
        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        using var timeoutCancellation = new CancellationTokenSource();
        var timeoutTask = Task.Delay(
            TimeSpan.FromSeconds(_options.ProbeTimeoutSeconds),
            timeoutCancellation.Token);
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

        if (completed != outputPumpsTask || capture.LimitExceeded.IsCompleted)
        {
            capture.StopStoring();
            try
            {
                await processTreeController.TerminateAsync(
                        process,
                        containment,
                        TimeSpan.FromSeconds(_options.TerminationGraceSeconds),
                        null)
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

            await outputPumpsTask.ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new PowerShellRuntimeValidationException(
                capture.LimitExceeded.IsCompleted
                    ? "The PowerShell runtime probe exceeded its output limit."
                    : "The PowerShell runtime probe timed out.");
        }

        await outputPumpsTask.ConfigureAwait(false);
        var output = capture.Snapshot();
        if (process.ExitCode != 0 ||
            output.StandardOutputTruncated ||
            output.StandardErrorTruncated ||
            !string.IsNullOrWhiteSpace(output.StandardError))
        {
            throw new PowerShellRuntimeValidationException(
                "The PowerShell runtime probe returned an invalid result.");
        }

        return ParseAndValidate(executablePath, output.StandardOutput);
    }

    internal PowerShellRuntimeInfo ParseAndValidate(string executablePath, string json)
    {
        RuntimeProbePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RuntimeProbePayload>(
                json.Trim(),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                });
        }
        catch (JsonException exception)
        {
            throw new PowerShellRuntimeValidationException(
                "The PowerShell runtime probe returned malformed JSON.",
                exception);
        }

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.Version) ||
            string.IsNullOrWhiteSpace(payload.PSEdition) ||
            string.IsNullOrWhiteSpace(payload.Platform) ||
            string.IsNullOrWhiteSpace(payload.OperatingSystem) ||
            string.IsNullOrWhiteSpace(payload.Architecture))
        {
            throw new PowerShellRuntimeValidationException(
                "The PowerShell runtime probe omitted required metadata.");
        }

        var versionText = payload.Version.Trim();
        var separator = versionText.IndexOf('-', StringComparison.Ordinal);
        var stableVersionText = separator < 0 ? versionText : versionText[..separator];
        if (!Version.TryParse(stableVersionText, out var version))
        {
            throw new PowerShellRuntimeValidationException(
                "The PowerShell runtime probe returned an invalid version.");
        }

        var isPreview = separator >= 0;
        if (!string.Equals(payload.PSEdition, "Core", StringComparison.Ordinal))
        {
            throw new PowerShellRuntimeValidationException(
                "Only PowerShell Core runtimes are supported.");
        }

        if (!string.Equals(payload.Platform, "Win32NT", StringComparison.Ordinal))
        {
            throw new PowerShellRuntimeValidationException(
                "Only Windows PowerShell Core runtimes are supported.");
        }

        if (!Version.TryParse(_options.MinimumVersion, out var minimumVersion) ||
            version < minimumVersion)
        {
            throw new PowerShellRuntimeValidationException(
                "The PowerShell runtime version is below the configured minimum.");
        }

        if (isPreview && !_options.AllowPreviewVersion)
        {
            throw new PowerShellRuntimeValidationException(
                "Preview PowerShell runtimes are disabled.");
        }

        if (_options.Require64Bit &&
            payload.Architecture is not ("X64" or "Arm64"))
        {
            throw new PowerShellRuntimeValidationException(
                "A 64-bit PowerShell runtime is required.");
        }

        return new PowerShellRuntimeInfo(
            Path.GetFullPath(executablePath),
            version,
            payload.PSEdition,
            payload.Platform,
            payload.OperatingSystem,
            payload.Architecture,
            isPreview);
    }

    private static void ValidateExecutablePath(string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath) ||
            !string.Equals(
                Path.GetFileName(executablePath),
                "pwsh.exe",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executablePath))
        {
            throw new PowerShellRuntimeValidationException(
                "The PowerShell executable candidate is invalid.");
        }
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

    private sealed record RuntimeProbePayload(
        string? Version,
        string? PSEdition,
        string? Platform,
        [property: JsonPropertyName("OS")] string? OperatingSystem,
        string? Architecture);
}
