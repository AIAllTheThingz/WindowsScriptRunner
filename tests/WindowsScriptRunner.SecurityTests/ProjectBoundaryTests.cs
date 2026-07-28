using System.Reflection;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;

namespace WindowsScriptRunner.SecurityTests;

public sealed class ProjectBoundaryTests
{
    public static TheoryData<string, string> ForbiddenReferences => new()
    {
        { "WindowsScriptRunner.Web", "WindowsScriptRunner.PowerShell" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Infrastructure" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Web" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Worker" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.PowerShell" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Reporting" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void AssemblyDoesNotDirectlyReferenceForbiddenProject(string assemblyName, string forbiddenName)
    {
        var references = Assembly.Load(assemblyName).GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name == forbiddenName);
    }

    [Fact]
    public void DomainReferencesNoSolutionOrProhibitedFrameworkAssembly()
    {
        var references = typeof(Domain.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
        string[] prohibitedPrefixes =
        [
            "WindowsScriptRunner.",
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.Data.SqlClient",
            "System.Data.SqlClient",
        ];

        Assert.DoesNotContain(
            references,
            reference => prohibitedPrefixes.Any(
                prefix => reference.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void ContractsReferenceNoDomainAndContainNoRepositoryImplementation()
    {
        var assembly = typeof(Contracts.AssemblyMarker).Assembly;

        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name == "WindowsScriptRunner.Domain");
        Assert.DoesNotContain(
            assembly.GetExportedTypes(),
            type => type.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DomainExposesNoObviousRawSecretProperty()
    {
        string[] prohibitedNames = ["Password", "RawSecret", "SecretValue", "CredentialValue"];

        Assert.DoesNotContain(
            typeof(Domain.AssemblyMarker).Assembly.GetExportedTypes()
                .SelectMany(type => type.GetProperties()),
            property => prohibitedNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void SensitiveRepresentationsAreRedacted()
    {
        var parameter = new JobParameter(
            "Credential",
            "external-reference-1",
            ScriptParameterType.SecureReference,
            isSensitive: true);
        var credential = new CredentialReference(
            CredentialReferenceId.New(),
            "ExternalVault",
            "vault/private/path",
            "Automation credential",
            DateTimeOffset.UtcNow,
            new UserIdentity("DOMAIN\\user"));

        Assert.DoesNotContain("external-reference-1", parameter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("vault/private/path", credential.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TraversalAndPublishedVersionMutationAreRejected()
    {
        Assert.Throws<InvalidScriptVersionException>(
            () => CreateVersion("../unsafe.ps1"));
        var version = CreateVersion("scripts/safe.ps1");
        version.Publish();

        Assert.Throws<InvalidScriptVersionException>(
            () => version.AddParameterDefinition(
                new ScriptParameterDefinition(
                    ScriptParameterDefinitionId.New(),
                    "Mode",
                    "Mode",
                    null,
                    ScriptParameterType.String,
                    false,
                    null,
                    [],
                    false)));
    }

    private static ScriptVersion CreateVersion(string path) =>
        new(
            ScriptVersionId.New(),
            ScriptVersionNumber.Parse("1.0.0"),
            path,
            new string('a', 64),
            null,
            "7.4",
            30,
            [ExecutionPhase.Validation],
            [],
            DateTimeOffset.UtcNow,
            new UserIdentity("DOMAIN\\user"));
}
