using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

            if (Directory.Exists(path) || File.Exists(path))
            {
                throw new PowerShellExecutionException(
                    "The PowerShell execution working directory already exists.");
            }

            Directory.CreateDirectory(path);
            return path;
        }
        catch (PowerShellExecutionException)
        {
            DeleteReservationAfterFailedCreate(reservationPath, reservationCreated);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            DeleteReservationAfterFailedCreate(reservationPath, reservationCreated);
            throw new PowerShellExecutionException(
                "The PowerShell execution working directory could not be reserved.",
                exception);
        }
    }

    public async Task DeleteAsync(string path)
    {
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
