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

    [Fact]
    public async Task DirectoryClaimPreventsReplacementUntilCleanup()
    {
        var setup = CreateWorkingDirectory();
        var executionId = PowerShellExecutionId.New();
        var path = setup.WorkingDirectory.Create(executionId);
        try
        {
            var exception = Record.Exception(() => Directory.Delete(path));

            Assert.True(
                exception is IOException or UnauthorizedAccessException,
                $"Expected an access failure, but received {exception?.GetType().Name ?? "none"}.");
            Assert.True(Directory.Exists(path));
        }
        finally
        {
            await setup.WorkingDirectory.DeleteAsync(path);
            DeleteRoot(setup.Root);
        }
    }

    [Fact]
    public void ExistingDirectoryCannotWinExecutionPathClaim()
    {
        var setup = CreateWorkingDirectory();
        var executionId = PowerShellExecutionId.New();
        var path = Path.Combine(setup.WorkingRoot, executionId.ToString());
        Directory.CreateDirectory(path);
        var sentinel = Path.Combine(path, "sentinel.txt");
        File.WriteAllText(sentinel, "existing");
        try
        {
            Assert.Throws<PowerShellExecutionException>(
                () => setup.WorkingDirectory.Create(executionId));
            Assert.True(File.Exists(sentinel));
            Assert.False(File.Exists(path + ".reservation"));
        }
        finally
        {
            DeleteRoot(setup.Root);
        }
    }

    [Fact]
    public void ReparsePointCannotWinExecutionPathClaim()
    {
        var setup = CreateWorkingDirectory();
        var executionId = PowerShellExecutionId.New();
        var path = Path.Combine(setup.WorkingRoot, executionId.ToString());
        var outside = Path.Combine(setup.Root, "outside");
        Directory.CreateDirectory(outside);
        JunctionTestSupport.Create(path, outside);
        try
        {
            Assert.Throws<PowerShellExecutionException>(
                () => setup.WorkingDirectory.Create(executionId));
            Assert.True(
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0);
            Assert.False(File.Exists(path + ".reservation"));
        }
        finally
        {
            Directory.Delete(path, recursive: false);
            DeleteRoot(setup.Root);
        }
    }

    private static WorkingDirectorySetup CreateWorkingDirectory()
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
        return new WorkingDirectorySetup(
            root,
            workingRoot,
            new ExecutionWorkingDirectory(
                Options.Create(options),
                NullLogger<ExecutionWorkingDirectory>.Instance));
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record WorkingDirectorySetup(
        string Root,
        string WorkingRoot,
        ExecutionWorkingDirectory WorkingDirectory);
}
