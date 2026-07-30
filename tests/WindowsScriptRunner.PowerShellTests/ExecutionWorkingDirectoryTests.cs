using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

public sealed class ExecutionWorkingDirectoryTests
{
    [Fact]
    public async Task ExecutionIdRemainsExclusivelyReservedUntilCleanup()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WindowsScriptRunner.PowerShellTests",
            Guid.NewGuid().ToString("N"));
        var allowedRoot = Path.Combine(root, "allowed");
        var workingRoot = Path.Combine(root, "working");
        Directory.CreateDirectory(allowedRoot);
        Directory.CreateDirectory(workingRoot);
        var options = PowerShellIntegrationFixture.CreateOptions(
            allowedRoot,
            workingRoot);
        var workingDirectory = new ExecutionWorkingDirectory(
            Options.Create(options),
            NullLogger<ExecutionWorkingDirectory>.Instance);
        var executionId = PowerShellExecutionId.New();
        var path = workingDirectory.Create(executionId);
        try
        {
            Directory.Delete(path);

            Assert.Throws<PowerShellExecutionException>(
                () => workingDirectory.Create(executionId));
        }
        finally
        {
            await workingDirectory.DeleteAsync(path);
            var reservationExists = File.Exists(path + ".reservation");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Assert.False(reservationExists);
        }
    }
}
