using System.Reflection;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Reports;
using WindowsScriptRunner.Reporting;

namespace WindowsScriptRunner.UnitTests;

public sealed class Phase7ReportingTests
{
    private static readonly DateTimeOffset StartedUtc =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
    private readonly LocalHostInventoryReportParser _parser = new();

    [Fact]
    public void ValidReviewedOutputProducesTypedNormalizedReport()
    {
        var report = _parser.Parse(Result(ValidJson(
            collectedUtc: "2026-07-30T14:00:00.5000000+02:00")));

        Assert.Equal("1.0", JobReport.LocalHostInventorySchemaVersion);
        Assert.Equal("WORKER-01", report.ComputerName);
        Assert.Equal("Microsoft Windows 11", report.OsDescription);
        Assert.Equal("10.0.26100", report.OsVersion);
        Assert.Equal("X64", report.OsArchitecture);
        Assert.Equal("7.4.0", report.PowerShellVersion);
        Assert.Equal(
            "2026-07-30T12:00:00.5000000+00:00",
            report.CollectedUtc.ToString("O"));
    }

    [Fact]
    public void ExactPropertiesMayAppearInAReviewedDifferentOrder()
    {
        var json =
            """
            {"collectedUtc":"2026-07-30T12:00:00.5000000Z","powerShell":{"version":"7.4.0"},"os":{"architecture":"Arm64","version":"10.0.26100","description":"Windows"},"computerName":"WORKER-01","schemaVersion":"1.0"}
            """;

        var report = _parser.Parse(Result(json));

        Assert.Equal("Arm64", report.OsArchitecture);
    }

    public static TheoryData<string> InvalidDocuments => new()
    {
        ValidJson().Replace(
            "\"schemaVersion\":\"1.0\"",
            "\"schemaVersion\":\"1.0\",\"unknown\":true",
            StringComparison.Ordinal),
        ValidJson().Replace(
            "\"description\":\"Microsoft Windows 11\"",
            "\"description\":\"Microsoft Windows 11\",\"unknown\":true",
            StringComparison.Ordinal),
        ValidJson().Replace(
            "\"schemaVersion\":\"1.0\"",
            "\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\"",
            StringComparison.Ordinal),
        ValidJson().Replace(
            "\"version\":\"10.0.26100\"",
            "\"version\":\"10.0.26100\",\"version\":\"10.0.26100\"",
            StringComparison.Ordinal),
        ValidJson() + "{}",
        "/* comment */" + ValidJson(),
        ValidJson().Replace(
            "\"collectedUtc\":\"2026-07-30T12:00:00.5000000Z\"}",
            "\"collectedUtc\":\"2026-07-30T12:00:00.5000000Z\",}",
            StringComparison.Ordinal),
        ValidJson().Replace("schemaVersion", "SchemaVersion", StringComparison.Ordinal),
        ValidJson().Replace(
            "\"computerName\":\"WORKER-01\"",
            "\"computerName\":null",
            StringComparison.Ordinal),
        ValidJson().Replace(
            "\"computerName\":\"WORKER-01\"",
            "\"computerName\":42",
            StringComparison.Ordinal),
        ValidJson().Replace(
            "\"powerShell\":{\"version\":\"7.4.0\"},",
            string.Empty,
            StringComparison.Ordinal),
        "[]",
        "{\"schemaVersion\":\"1.0\"}",
        ValidJson().Replace(
            "\"description\":\"Microsoft Windows 11\"",
            "\"description\":{\"nested\":{\"too\":{\"deep\":true}}}",
            StringComparison.Ordinal),
    };

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public void NonExactJsonDocumentsAreRejected(string json)
    {
        Assert.Throws<LocalHostInventoryReportValidationException>(
            () => _parser.Parse(Result(json)));
    }

    [Theory]
    [InlineData("2.0", "WORKER-01", "10.0.26100", "X64", "7.4.0")]
    [InlineData("1.0", "-WORKER", "10.0.26100", "X64", "7.4.0")]
    [InlineData("1.0", "WORKER_01", "10.0.26100", "X64", "7.4.0")]
    [InlineData("1.0", "WORKER-01", "10..0", "X64", "7.4.0")]
    [InlineData("1.0", "WORKER-01", "10.0.26100", "Mips", "7.4.0")]
    [InlineData("1.0", "WORKER-01", "10.0.26100", "x64", "7.4.0")]
    [InlineData("1.0", "WORKER-01", "10.0.26100", "X64", "7.3.9")]
    [InlineData("1.0", "WORKER-01", "10.0.26100", "X64", "7.4")]
    public void InvalidSchemaAndInventoryValuesAreRejected(
        string schemaVersion,
        string computerName,
        string osVersion,
        string architecture,
        string powerShellVersion)
    {
        var json = ValidJson(
            schemaVersion,
            computerName,
            osVersion,
            architecture,
            powerShellVersion);

        Assert.Throws<LocalHostInventoryReportValidationException>(
            () => _parser.Parse(Result(json)));
    }

    [Theory]
    [InlineData("2026-07-30T12:00:00Z")]
    [InlineData("2026-07-30T12:00:00.5000000")]
    [InlineData("not-a-timestamp")]
    [InlineData("2026-07-30T11:59:54.0000000Z")]
    [InlineData("2026-07-30T12:00:06.0000001Z")]
    public void InvalidCollectionTimestampsAreRejected(string timestamp)
    {
        Assert.Throws<LocalHostInventoryReportValidationException>(
            () => _parser.Parse(Result(ValidJson(collectedUtc: timestamp))));
    }

    [Fact]
    public void OversizedDocumentAndStringsAreRejected()
    {
        Assert.Throws<LocalHostInventoryReportValidationException>(
            () => _parser.Parse(Result(
                ValidJson() +
                new string(' ', LocalHostInventoryReportParser.MaximumDocumentUtf8Bytes))));
        Assert.Throws<LocalHostInventoryReportValidationException>(
            () => _parser.Parse(Result(
                ValidJson(computerName: new string('A', 64)))));
        Assert.Throws<LocalHostInventoryReportValidationException>(
            () => _parser.Parse(Result(
                ValidJson(osDescription: new string('A', 257)))));
    }

    [Theory]
    [InlineData(false, 0, false, false, "")]
    [InlineData(true, 1, false, false, "")]
    [InlineData(true, 0, true, false, "")]
    [InlineData(true, 0, false, true, "")]
    [InlineData(true, 0, false, false, "unexpected stderr")]
    public void OnlyCompleteSuccessfulResultsWithWhitespaceStderrAreAccepted(
        bool exited,
        int exitCode,
        bool stdoutTruncated,
        bool stderrTruncated,
        string stderr)
    {
        var result = Result(
            ValidJson(),
            exited,
            exitCode,
            stdoutTruncated,
            stderrTruncated,
            stderr);

        Assert.Throws<LocalHostInventoryReportValidationException>(
            () => _parser.Parse(result));
    }

    [Fact]
    public void WhitespaceSuccessStderrIsAccepted()
    {
        var report = _parser.Parse(Result(
            ValidJson(),
            exited: true,
            exitCode: 0,
            stdoutTruncated: false,
            stderrTruncated: false,
            stderr: " \r\n\t"));

        Assert.Equal("WORKER-01", report.ComputerName);
    }

    [Fact]
    public void RejectedOutputIsNotEchoedInExceptionMessages()
    {
        const string sensitiveValue = "PRIVATE-WORKER-VALUE";
        var exception = Assert.Throws<LocalHostInventoryReportValidationException>(
            () => _parser.Parse(Result(
                ValidJson(computerName: sensitiveValue) + "{}")));

        Assert.DoesNotContain(
            sensitiveValue,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalDigestIsDeterministicAndContentSensitive()
    {
        var canonical = Canonical();

        var first = LocalHostInventoryCanonicalizer.CreateSha256(canonical);
        var second = LocalHostInventoryCanonicalizer.CreateSha256(canonical);
        var changed = LocalHostInventoryCanonicalizer.CreateSha256(
            canonical with
            {
                ComputerName = "WORKER-02",
            });

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void DurableReportIdentityAndModelAreImmutableAndDeterministic()
    {
        var jobId = JobId.New();
        var payload = Payload();
        var report = JobReport.CreateLocalHostInventory(
            jobId,
            ScriptDefinitionId.New(),
            ScriptVersionId.New(),
            WorkerNodeId.New(),
            JobLeaseId.New(),
            1,
            Guid.NewGuid(),
            StartedUtc.AddSeconds(1),
            StartedUtc,
            payload,
            new string('a', 64));

        Assert.Equal(
            JobReport.CreateDeterministicId(jobId),
            JobReport.CreateDeterministicId(jobId));
        Assert.NotEqual(
            report.Id,
            JobReport.CreateDeterministicId(JobId.New()));
        Assert.All(
            typeof(JobReport).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.False(property.CanWrite));
        Assert.All(
            typeof(LocalHostInventoryReportPayload).GetProperties(
                BindingFlags.Public | BindingFlags.Instance),
            property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void DurableReportRejectsInvalidPayloadAndTimestampOrdering()
    {
        Assert.Throws<DomainValidationException>(
            () => new LocalHostInventoryReportPayload(
                "WORKER-01",
                "Windows",
                "10.0.0",
                (InventoryOsArchitecture)999,
                "7.4.0"));
        Assert.Throws<DomainValidationException>(
            () => new LocalHostInventoryReportPayload(
                "WORKER-01",
                "Windows",
                "10.0.0",
                InventoryOsArchitecture.X64,
                "7.3.9"));
        Assert.Throws<DomainValidationException>(
            () => JobReport.CreateLocalHostInventory(
                JobId.New(),
                ScriptDefinitionId.New(),
                ScriptVersionId.New(),
                WorkerNodeId.New(),
                JobLeaseId.New(),
                1,
                Guid.NewGuid(),
                StartedUtc,
                StartedUtc.AddSeconds(6),
                Payload(),
                new string('a', 64)));
    }

    private static LocalHostInventoryProcessResult Result(
        string json,
        bool exited = true,
        int exitCode = 0,
        bool stdoutTruncated = false,
        bool stderrTruncated = false,
        string stderr = "") =>
        new(
            Guid.NewGuid(),
            StartedUtc,
            StartedUtc.AddSeconds(1),
            exitCode,
            json,
            stderr,
            stdoutTruncated,
            stderrTruncated,
            exited);

    private static string ValidJson(
        string schemaVersion = "1.0",
        string computerName = "WORKER-01",
        string osVersion = "10.0.26100",
        string architecture = "X64",
        string powerShellVersion = "7.4.0",
        string collectedUtc = "2026-07-30T12:00:00.5000000Z",
        string osDescription = "Microsoft Windows 11") =>
        $$"""
        {"schemaVersion":"{{schemaVersion}}","computerName":"{{computerName}}","os":{"description":"{{osDescription}}","version":"{{osVersion}}","architecture":"{{architecture}}"},"powerShell":{"version":"{{powerShellVersion}}"},"collectedUtc":"{{collectedUtc}}"}
        """;

    private static LocalHostInventoryCanonicalReport Canonical() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            StartedUtc,
            "WORKER-01",
            "Microsoft Windows 11",
            "10.0.26100",
            "X64",
            "7.4.0");

    private static LocalHostInventoryReportPayload Payload() =>
        new(
            "WORKER-01",
            "Microsoft Windows 11",
            "10.0.26100",
            InventoryOsArchitecture.X64,
            "7.4.0");
}
