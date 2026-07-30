using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsScriptRunner.PowerShell;

namespace WindowsScriptRunner.PowerShellTests;

public sealed class PowerShellLocatorAndProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "WindowsScriptRunner.LocatorTests",
        Guid.NewGuid().ToString("N"));

    public PowerShellLocatorAndProbeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ExplicitPathWinsAndResultIsCached()
    {
        var explicitPath = CreateExecutable("explicit");
        var environmentPath = CreateExecutable("environment");
        var probe = new FakeRuntimeProbe();
        var locator = CreateLocator(
            new PowerShellExecutionOptions { ExecutablePath = explicitPath },
            probe,
            new PowerShellCandidateSet(environmentPath, [], []));

        var first = await locator.LocateAsync(CancellationToken.None);
        var second = await locator.LocateAsync(CancellationToken.None);

        Assert.Equal(explicitPath, first.ExecutablePath);
        Assert.Same(first, second);
        Assert.Equal([explicitPath], probe.ProbedPaths);
    }

    [Fact]
    public async Task ConcurrentCallsShareSafelyPublishedCachedRuntime()
    {
        var explicitPath = CreateExecutable("explicit");
        var probe = new FakeRuntimeProbe(delay: TimeSpan.FromMilliseconds(50));
        var locator = CreateLocator(
            new PowerShellExecutionOptions { ExecutablePath = explicitPath },
            probe,
            new PowerShellCandidateSet(null, [], []));

        var runtimes = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => locator.LocateAsync(CancellationToken.None)));

        Assert.All(runtimes, runtime => Assert.Same(runtimes[0], runtime));
        Assert.Equal([explicitPath], probe.ProbedPaths);
    }

    [Fact]
    public async Task EnvironmentOverrideIsSecondAndAuthoritative()
    {
        var environmentPath = CreateExecutable("environment");
        var pathCandidate = CreateExecutable("path");
        var probe = new FakeRuntimeProbe();
        var locator = CreateLocator(
            new PowerShellExecutionOptions(),
            probe,
            new PowerShellCandidateSet(environmentPath, [pathCandidate], []));

        var runtime = await locator.LocateAsync(CancellationToken.None);

        Assert.Equal(environmentPath, runtime.ExecutablePath);
        Assert.Equal([environmentPath], probe.ProbedPaths);
    }

    [Fact]
    public async Task PathAndStandardLocationsAreSearchedAndDeduplicated()
    {
        var pathCandidate = CreateExecutable("path");
        var standardCandidate = CreateExecutable("standard");
        var probe = new FakeRuntimeProbe(
            new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase)
            {
                [pathCandidate] = new Version(7, 4, 2),
                [standardCandidate] = new Version(7, 5, 1),
            });
        var locator = CreateLocator(
            new PowerShellExecutionOptions(),
            probe,
            new PowerShellCandidateSet(
                null,
                [pathCandidate, pathCandidate],
                [standardCandidate, pathCandidate]));

        var runtime = await locator.LocateAsync(CancellationToken.None);

        Assert.Equal(standardCandidate, runtime.ExecutablePath);
        Assert.Equal(2, probe.ProbedPaths.Count);
    }

    [Fact]
    public async Task StandardLocationWorksWithoutPathCandidate()
    {
        var standardCandidate = CreateExecutable("standard");
        var locator = CreateLocator(
            new PowerShellExecutionOptions(),
            new FakeRuntimeProbe(),
            new PowerShellCandidateSet(null, [], [standardCandidate]));

        Assert.Equal(
            standardCandidate,
            (await locator.LocateAsync(CancellationToken.None)).ExecutablePath);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("missing")]
    [InlineData("powershell")]
    public async Task InvalidConfiguredExecutableFails(string kind)
    {
        var path = kind switch
        {
            "relative" => "pwsh.exe",
            "missing" => Path.Combine(_root, "missing", "pwsh.exe"),
            _ => CreateExecutable("legacy", "powershell.exe"),
        };
        var locator = CreateLocator(
            new PowerShellExecutionOptions { ExecutablePath = path },
            new FakeRuntimeProbe(),
            new PowerShellCandidateSet(null, [], []));

        await Assert.ThrowsAsync<PowerShellRuntimeNotFoundException>(
            () => locator.LocateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StableRuntimeIsPreferredToPreviewRuntime()
    {
        var stable = CreateExecutable("stable");
        var preview = CreateExecutable("preview");
        var probe = new FakeRuntimeProbe(
            new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase)
            {
                [stable] = new Version(7, 5, 0),
                [preview] = new Version(7, 6, 0),
            },
            preview);
        var locator = CreateLocator(
            new PowerShellExecutionOptions { AllowPreviewVersion = true },
            probe,
            new PowerShellCandidateSet(null, [preview, stable], []));

        Assert.Equal(
            stable,
            (await locator.LocateAsync(CancellationToken.None)).ExecutablePath);
    }

    [Fact]
    public void RuntimeProbeRejectsMalformedOrIncompatibleMetadata()
    {
        var stableProbe = CreateParser(new PowerShellExecutionOptions());
        Assert.Throws<PowerShellRuntimeValidationException>(
            () => stableProbe.ParseAndValidate(
                Path.Combine(_root, "pwsh.exe"),
                "{broken"));
        Assert.Throws<PowerShellRuntimeValidationException>(
            () => stableProbe.ParseAndValidate(
                Path.Combine(_root, "pwsh.exe"),
                Payload("7.3.9")));
        Assert.Throws<PowerShellRuntimeValidationException>(
            () => stableProbe.ParseAndValidate(
                Path.Combine(_root, "pwsh.exe"),
                Payload("7.4.0", edition: "Desktop")));
        Assert.Throws<PowerShellRuntimeValidationException>(
            () => stableProbe.ParseAndValidate(
                Path.Combine(_root, "pwsh.exe"),
                Payload("7.4.0", architecture: "X86")));
        Assert.Throws<PowerShellRuntimeValidationException>(
            () => stableProbe.ParseAndValidate(
                Path.Combine(_root, "pwsh.exe"),
                Payload("7.6.0-preview.1")));
    }

    [Fact]
    public void PreviewRuntimeRequiresExplicitOptIn()
    {
        var probe = CreateParser(
            new PowerShellExecutionOptions { AllowPreviewVersion = true });

        var runtime = probe.ParseAndValidate(
            Path.Combine(_root, "pwsh.exe"),
            Payload("7.6.0-preview.1"));

        Assert.True(runtime.IsPreview);
        Assert.Equal(new Version(7, 6, 0), runtime.Version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PowerShellExecutableLocator CreateLocator(
        PowerShellExecutionOptions options,
        IPowerShellRuntimeProbe probe,
        PowerShellCandidateSet candidates) =>
        new(
            Options.Create(options),
            probe,
            new FakeCandidateSource(candidates),
            NullLogger<PowerShellExecutableLocator>.Instance);

    private PowerShellRuntimeProbe CreateParser(PowerShellExecutionOptions options) =>
        new(
            Options.Create(options),
            new ProcessTreeController(NullLogger<ProcessTreeController>.Instance));

    private string CreateExecutable(string directory, string fileName = "pwsh.exe")
    {
        var path = Path.Combine(_root, directory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return Path.GetFullPath(path);
    }

    private static string Payload(
        string version,
        string edition = "Core",
        string architecture = "X64") =>
        $$"""{"Version":"{{version}}","PSEdition":"{{edition}}","Platform":"Win32NT","OS":"Windows","Architecture":"{{architecture}}"}""";

    private sealed class FakeCandidateSource(PowerShellCandidateSet candidates) :
        IPowerShellCandidateSource
    {
        public PowerShellCandidateSet GetCandidates() => candidates;
    }

    private sealed class FakeRuntimeProbe(
        IReadOnlyDictionary<string, Version>? versions = null,
        string? previewPath = null,
        TimeSpan? delay = null) : IPowerShellRuntimeProbe
    {
        private readonly List<string> _probedPaths = [];

        public IReadOnlyList<string> ProbedPaths => _probedPaths;

        public async Task<PowerShellRuntimeInfo> ProbeAsync(
            string executablePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _probedPaths.Add(executablePath);
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            var version = versions is not null &&
                versions.TryGetValue(executablePath, out var configured)
                ? configured
                : new Version(7, 4, 0);
            return new PowerShellRuntimeInfo(
                executablePath,
                version,
                "Core",
                "Win32NT",
                "Windows",
                "X64",
                string.Equals(
                    executablePath,
                    previewPath,
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}
