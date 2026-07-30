using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace WindowsScriptRunner.PowerShell;

internal interface IProcessTreeController
{
    ProcessTreeContainment Attach(Process process);

    Task TerminateAsync(
        Process process,
        ProcessTreeContainment containment,
        TimeSpan gracePeriod,
        PowerShellExecutionId? executionId);
}

internal sealed class ProcessTreeController(
    ILogger<ProcessTreeController> logger) : IProcessTreeController
{
    public ProcessTreeContainment Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning(
                "Windows Job Object containment is unavailable; process-tree fallback is active.");
            return new ProcessTreeContainment(null);
        }

        SafeJobHandle? job = null;
        try
        {
            job = new SafeJobHandle(
                NativeMethods.CreateJobObject(nint.Zero, null));
            if (job.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose,
                },
            };
            if (!NativeMethods.SetInformationJobObject(
                    job,
                    JobObjectInformationClass.ExtendedLimitInformation,
                    ref information,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!NativeMethods.AssignProcessToJobObject(job, process.SafeHandle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            logger.LogDebug(
                "PowerShell process {ProcessId} assigned to a kill-on-close Job Object.",
                process.Id);
            return new ProcessTreeContainment(job);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            job?.Dispose();
            logger.LogWarning(
                "PowerShell process {ProcessId} is using full process-tree termination fallback.",
                SafeProcessId(process));
            return new ProcessTreeContainment(null);
        }
    }

    public async Task TerminateAsync(
        Process process,
        ProcessTreeContainment containment,
        TimeSpan gracePeriod,
        PowerShellExecutionId? executionId)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(containment);
        Exception? terminationError = null;

        try
        {
            if (containment.JobHandle is not null &&
                !containment.JobHandle.IsInvalid &&
                !NativeMethods.TerminateJobObject(containment.JobHandle, 1))
            {
                terminationError = new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!HasExited(process))
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            terminationError ??= exception;
        }

        if (await WaitForExitAsync(process, gracePeriod).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception exception) when (
            exception is Win32Exception or NotSupportedException)
        {
            terminationError ??= exception;
        }

        if (await WaitForExitAsync(process, TimeSpan.FromSeconds(1)).ConfigureAwait(false))
        {
            return;
        }

        var message = executionId is null
            ? "The PowerShell process tree could not be terminated."
            : $"PowerShell execution {executionId} process {SafeProcessId(process)} could not be terminated.";
        throw terminationError is null
            ? new PowerShellProcessTerminationException(message)
            : new PowerShellProcessTerminationException(message, terminationError);
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        if (HasExited(process))
        {
            return true;
        }

        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        using var timeoutCancellation = new CancellationTokenSource();
        var timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
        var completed = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);
        timeoutCancellation.Cancel();
        return completed == exitTask;
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}

internal sealed class ProcessTreeContainment(SafeJobHandle? jobHandle) : IDisposable
{
    internal SafeJobHandle? JobHandle { get; } = jobHandle;

    public void Dispose() => JobHandle?.Dispose();
}

internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeJobHandle(nint handle)
        : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}

internal enum JobObjectInformationClass
{
    ExtendedLimitInformation = 9,
}

[StructLayout(LayoutKind.Sequential)]
internal struct JobObjectBasicLimitInformation
{
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public uint LimitFlags;
    public nuint MinimumWorkingSetSize;
    public nuint MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public nuint Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IoCounters
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JobObjectExtendedLimitInformation
{
    public JobObjectBasicLimitInformation BasicLimitInformation;
    public IoCounters IoInfo;
    public nuint ProcessMemoryLimit;
    public nuint JobMemoryLimit;
    public nuint PeakProcessMemoryUsed;
    public nuint PeakJobMemoryUsed;
}

internal static partial class NativeMethods
{
    internal const uint JobObjectLimitKillOnJobClose = 0x00002000;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateJobObject(nint jobAttributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetInformationJobObject(
        SafeJobHandle job,
        JobObjectInformationClass informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AssignProcessToJobObject(
        SafeJobHandle job,
        SafeProcessHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateJobObject(SafeJobHandle job, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}
