using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.Automation;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

[Collection(PowerShellIntegrationCollection.Name)]
public sealed class ReviewedPackageIntegrationTests(
    PowerShellIntegrationFixture fixture)
{
    [Fact]
    public async Task ReviewedLocalHostInventoryProducesOneBoundedVersionedJsonDocument()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "WindowsScriptRunner.Phase6.PowerShellTests",
            Guid.NewGuid().ToString("N"));
        var allowedRoot = Path.Combine(root, "allowed");
        var workingRoot = Path.Combine(root, "working");
        var scriptPath = Path.Combine(
            allowedRoot,
            LocalHostInventoryPackageMetadata.RelativeScriptPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        Directory.CreateDirectory(workingRoot);
        File.Copy(SourceScriptPath(), scriptPath);

        try
        {
            var options = PowerShellIntegrationFixture.CreateOptions(
                allowedRoot,
                workingRoot);
            options.MaximumTimeoutSeconds = 60;
            var wrappedOptions = Options.Create(options);
            var trustValidator = new PowerShellScriptTrustValidator(wrappedOptions);
            var factory = new ReviewedPowerShellArtifactFactory(
                wrappedOptions,
                trustValidator);
            var catalog = new LocalHostInventoryArtifactCatalog(factory);
            var definition =
                LocalHostInventoryPackageMetadata.CreateDefinition(DateTimeOffset.UtcNow);
            var trusted = catalog.Resolve(
                definition,
                Assert.Single(definition.Versions));
            var boundary = new PowerShellExecutionBoundary(
                wrappedOptions,
                new FixedRuntimeLocator(fixture.Runtime),
                trustValidator,
                new PowerShellArgumentValidator(),
                new ExecutionWorkingDirectory(
                    wrappedOptions,
                    NullLogger<ExecutionWorkingDirectory>.Instance),
                new ProcessTreeController(
                    NullLogger<ProcessTreeController>.Instance),
                NullLogger<PowerShellExecutionBoundary>.Instance);

            var result = await boundary.ExecuteAsync(
                new PowerShellExecutionRequest(
                    PowerShellExecutionId.New(),
                    trusted,
                    [],
                    TimeSpan.FromMinutes(1)),
                CancellationToken.None);

            Assert.Equal(PowerShellTerminationReason.Exited, result.TerminationReason);
            Assert.Equal(0, result.ExitCode);
            Assert.InRange(result.StandardOutputBytes, 1, 4096);
            Assert.False(result.StandardOutputTruncated);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var rootElement = document.RootElement;
            Assert.Equal(JsonValueKind.Object, rootElement.ValueKind);
            Assert.Equal(
                ["schemaVersion", "computerName", "os", "powerShell", "collectedUtc"],
                rootElement.EnumerateObject()
                    .Select(property => property.Name)
                    .ToArray());
            Assert.Equal("1.0", rootElement.GetProperty("schemaVersion").GetString());
            Assert.False(
                string.IsNullOrWhiteSpace(
                    rootElement.GetProperty("computerName").GetString()));
            Assert.Equal(
                ["description", "version", "architecture"],
                rootElement.GetProperty("os")
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .ToArray());
            Assert.Equal(
                ["version"],
                rootElement.GetProperty("powerShell")
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .ToArray());
            Assert.True(
                DateTimeOffset.TryParse(
                    rootElement.GetProperty("collectedUtc").GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out _));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string SourceScriptPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "WindowsScriptRunner.Automation",
                "Artifacts",
                "windows.local-host-inventory",
                "1.0.0",
                "Collect-LocalHostInventory.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate the reviewed Phase 6 artifact.");
    }
}
