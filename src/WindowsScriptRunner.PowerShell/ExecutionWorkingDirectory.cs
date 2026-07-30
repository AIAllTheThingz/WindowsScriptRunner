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
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new PowerShellExecutionException(
                "The PowerShell execution working directory already exists.");
        }

        Directory.CreateDirectory(path);
        return path;
    }

    public async Task DeleteAsync(string path)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
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
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new PowerShellExecutionException(
                    "The PowerShell working root cannot contain a reparse point.");
            }
        }
    }
}
