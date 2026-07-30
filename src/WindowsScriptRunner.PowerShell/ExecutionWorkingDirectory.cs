using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;

namespace WindowsScriptRunner.PowerShell;

internal interface IExecutionWorkingDirectory
{
    string Create(PowerShellExecutionId executionId);

    Task DeleteAsync(string path);
}

internal sealed class ExecutionWorkingDirectory(
    IOptions<PowerShellExecutionOptions> options,
    ILogger<ExecutionWorkingDirectory> logger) : IExecutionWorkingDirectory
{
    private readonly ConcurrentDictionary<string, WorkingDirectoryClaim> _directoryClaims =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _workingRoot = Path.GetFullPath(
        options.Value.WorkingRoot ??
        throw new PowerShellExecutionException(
            "The PowerShell working root is not configured."));

    public string Create(PowerShellExecutionId executionId)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        Directory.CreateDirectory(_workingRoot);
        RejectReparsePoints(_workingRoot);

        var path = Path.Combine(_workingRoot, executionId.ToString());
        var reservationPath = GetReservationPath(path);
        var reservationCreated = false;
        var directoryCreated = false;
        WorkingDirectoryClaim? directoryClaim = null;
        try
        {
            using (new FileStream(
                       reservationPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                reservationCreated = true;
            }

            CreateDirectoryExclusively(path);
            directoryCreated = true;
            directoryClaim = OpenDirectoryClaim(path);
            if (!_directoryClaims.TryAdd(path, directoryClaim))
            {
                throw new PowerShellExecutionException(
                    "The PowerShell execution working directory already exists.");
            }

            directoryClaim = null;
            return path;
        }
        catch (PowerShellExecutionException)
        {
            directoryClaim?.Dispose();
            DeleteDirectoryAfterFailedCreate(path, directoryCreated);
            DeleteReservationAfterFailedCreate(reservationPath, reservationCreated);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            directoryClaim?.Dispose();
            DeleteDirectoryAfterFailedCreate(path, directoryCreated);
            DeleteReservationAfterFailedCreate(reservationPath, reservationCreated);
            throw new PowerShellExecutionException(
                "The PowerShell execution working directory could not be reserved.",
                exception);
        }
    }

    public async Task DeleteAsync(string path)
    {
        if (_directoryClaims.TryRemove(path, out var directoryClaim))
        {
            try
            {
                directoryClaim.Dispose();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(
                    "PowerShell working-directory claim disposal failed for " +
                    "{WorkingDirectoryName}.",
                    Path.GetFileName(path));
            }
        }

        var reservationPath = GetReservationPath(path);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                if (File.Exists(reservationPath))
                {
                    File.Delete(reservationPath);
                }

                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 3)
                {
                    logger.LogError(
                        "PowerShell working-directory cleanup failed for {WorkingDirectoryName}.",
                        Path.GetFileName(path));
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
        }
    }

    private static string GetReservationPath(string path) => path + ".reservation";

    private static void CreateDirectoryExclusively(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PowerShellExecutionException(
                "The PowerShell execution working directory requires Windows.");
        }

        if (!WorkingDirectoryNativeMethods.CreateDirectory(
                ToNativePath(path),
                nint.Zero))
        {
            throw new PowerShellExecutionException(
                "The PowerShell execution working directory could not be claimed.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    private static WorkingDirectoryClaim OpenDirectoryClaim(string path)
    {
        var rawHandle = WorkingDirectoryNativeMethods.CreateFile(
            ToNativePath(path),
            WorkingDirectoryNativeMethods.FileReadAttributes,
            WorkingDirectoryNativeMethods.FileShareRead |
            WorkingDirectoryNativeMethods.FileShareWrite,
            nint.Zero,
            WorkingDirectoryNativeMethods.OpenExisting,
            WorkingDirectoryNativeMethods.FileFlagBackupSemantics |
            WorkingDirectoryNativeMethods.FileFlagOpenReparsePoint,
            nint.Zero);
        var error = Marshal.GetLastPInvokeError();
        var handle = new SafeFileHandle(rawHandle, ownsHandle: true);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new PowerShellExecutionException(
                "The PowerShell execution working directory could not be secured.",
                new Win32Exception(error));
        }

        try
        {
            if (!WorkingDirectoryNativeMethods.GetFileInformationByHandleEx(
                    handle,
                    FileInfoByHandleClass.FileAttributeTagInfo,
                    out var information,
                    (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
            {
                throw new PowerShellExecutionException(
                    "The PowerShell execution working directory could not be inspected.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
            }

            var attributes = (FileAttributes)information.FileAttributes;
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new PowerShellExecutionException(
                    "The PowerShell execution working directory claim is invalid.");
            }

            var claimFile = new FileStream(
                Path.Combine(path, ".wsr-claim"),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return new WorkingDirectoryClaim(handle, claimFile);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static string ToNativePath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? path
            : @"\\?\" + path;

    private void DeleteDirectoryAfterFailedCreate(string path, bool directoryCreated)
    {
        if (!directoryCreated)
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                "PowerShell working-directory failed-claim cleanup failed for " +
                "{WorkingDirectoryName}.",
                Path.GetFileName(path));
        }
    }

    private void DeleteReservationAfterFailedCreate(
        string reservationPath,
        bool reservationCreated)
    {
        if (!reservationCreated)
        {
            return;
        }

        try
        {
            File.Delete(reservationPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                "PowerShell working-directory reservation cleanup failed for " +
                "{WorkingDirectoryName}.",
                Path.GetFileNameWithoutExtension(reservationPath));
        }
    }

    private static void RejectReparsePoints(string path)
    {
        var root = Path.GetPathRoot(path) ??
            throw new PowerShellExecutionException(
                "The PowerShell working root has no local root.");
        var current = root;
        foreach (var component in path[root.Length..].Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new PowerShellExecutionException(
                    "The PowerShell working directory path could not be inspected.",
                    exception);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new PowerShellExecutionException(
                    "The PowerShell working root cannot contain a reparse point.");
            }
        }
    }
}

internal sealed class WorkingDirectoryClaim(
    SafeFileHandle directoryHandle,
    FileStream claimFile) : IDisposable
{
    public void Dispose()
    {
        try
        {
            claimFile.Dispose();
        }
        finally
        {
            directoryHandle.Dispose();
        }
    }
}

internal enum FileInfoByHandleClass
{
    FileAttributeTagInfo = 9,
}

[StructLayout(LayoutKind.Sequential)]
internal struct FileAttributeTagInfo
{
    public uint FileAttributes;
    public uint ReparseTag;
}

internal static partial class WorkingDirectoryNativeMethods
{
    internal const uint FileReadAttributes = 0x00000080;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;
    internal const uint FileFlagBackupSemantics = 0x02000000;
    internal const uint FileFlagOpenReparsePoint = 0x00200000;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateDirectory(string path, nint securityAttributes);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        FileInfoByHandleClass fileInformationClass,
        out FileAttributeTagInfo fileInformation,
        uint bufferSize);
}
