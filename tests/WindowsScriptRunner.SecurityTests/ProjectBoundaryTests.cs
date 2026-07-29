using System.Reflection;
using System.Xml.Linq;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Jobs;
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
    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        { "WindowsScriptRunner.Domain", [] },
        { "WindowsScriptRunner.Contracts", [] },
        {
            "WindowsScriptRunner.Application",
            ["WindowsScriptRunner.Contracts", "WindowsScriptRunner.Domain"]
        },
        {
            "WindowsScriptRunner.Infrastructure",
            [
                "WindowsScriptRunner.Application",
                "WindowsScriptRunner.Domain",
            ]
        },
        {
            "WindowsScriptRunner.PowerShell",
            [
                "WindowsScriptRunner.Application",
                "WindowsScriptRunner.Contracts",
                "WindowsScriptRunner.Domain",
            ]
        },
        {
            "WindowsScriptRunner.Reporting",
            [
                "WindowsScriptRunner.Application",
                "WindowsScriptRunner.Contracts",
                "WindowsScriptRunner.Domain",
            ]
        },
        {
            "WindowsScriptRunner.Web",
            [
                "WindowsScriptRunner.Application",
                "WindowsScriptRunner.Contracts",
                "WindowsScriptRunner.Infrastructure",
                "WindowsScriptRunner.Reporting",
            ]
        },
        {
            "WindowsScriptRunner.Worker",
            [
                "WindowsScriptRunner.Application",
                "WindowsScriptRunner.Contracts",
                "WindowsScriptRunner.Domain",
                "WindowsScriptRunner.Infrastructure",
                "WindowsScriptRunner.Reporting",
            ]
        },
    };

    public static TheoryData<string, string> ForbiddenReferences => new()
    {
        { "WindowsScriptRunner.Web", "WindowsScriptRunner.PowerShell" },
        { "WindowsScriptRunner.Infrastructure", "WindowsScriptRunner.PowerShell" },
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

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void ProjectFileReferencesMatchAllowedArchitecture(
        string projectName,
        string[] expectedReferences)
    {
        var actualReferences = ReadProjectReferences(projectName);

        Assert.Equal(
            expectedReferences.Order(StringComparer.Ordinal),
            actualReferences.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WebProjectFileDoesNotReferenceWorkerOrPowerShell()
    {
        var references = ReadProjectReferences("WindowsScriptRunner.Web");

        Assert.DoesNotContain("WindowsScriptRunner.Worker", references);
        Assert.DoesNotContain("WindowsScriptRunner.PowerShell", references);
    }

    [Fact]
    public void WorkerInfrastructureAndPowerShellProjectBoundariesRemainIsolated()
    {
        Assert.DoesNotContain(
            "WindowsScriptRunner.PowerShell",
            ReadProjectReferences("WindowsScriptRunner.Worker"));
        Assert.DoesNotContain(
            "WindowsScriptRunner.PowerShell",
            ReadProjectReferences("WindowsScriptRunner.Infrastructure"));
        Assert.DoesNotContain(
            "WindowsScriptRunner.Infrastructure",
            ReadProjectReferences("WindowsScriptRunner.PowerShell"));
    }

    [Fact]
    public void EfCoreAndSqlServerPackagesAreInfrastructureOnly()
    {
        var packageReferences = ReadSourcePackageReferences();
        var persistencePackages = packageReferences
            .Where(reference =>
                reference.PackageName.StartsWith(
                    "Microsoft.EntityFrameworkCore",
                    StringComparison.Ordinal) ||
                reference.PackageName == "Microsoft.Data.SqlClient" ||
                reference.PackageName == "Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore")
            .ToArray();

        Assert.NotEmpty(persistencePackages);
        Assert.All(
            persistencePackages,
            reference => Assert.Equal("WindowsScriptRunner.Infrastructure", reference.ProjectName));
        Assert.Contains(
            persistencePackages,
            reference => reference.PackageName == "Microsoft.EntityFrameworkCore.SqlServer");
    }

    [Theory]
    [InlineData("WindowsScriptRunner.Domain")]
    [InlineData("WindowsScriptRunner.Contracts")]
    public void PersistenceIndependentProjectsContainNoEfAttributes(string projectName)
    {
        var source = ReadProjectSource(projectName);
        string[] attributes = ["[Key", "[Column", "[Table", "[Owned", "[NotMapped", "[Index"];

        Assert.DoesNotContain(
            attributes,
            attribute => source.Contains(attribute, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("WindowsScriptRunner.Application")]
    [InlineData("WindowsScriptRunner.Web")]
    [InlineData("WindowsScriptRunner.Worker")]
    public void NonInfrastructureProjectsContainNoDbContext(string projectName)
    {
        var source = ReadProjectSource(projectName);

        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("WindowsScriptRunner.Web")]
    [InlineData("WindowsScriptRunner.Worker")]
    public void CompositionRootsDoNotReferenceSqlClientPackage(string projectName)
    {
        var packages = ReadPackageReferences(projectName);

        Assert.DoesNotContain("Microsoft.Data.SqlClient", packages);
    }

    [Fact]
    public void ProductionPersistenceSourceContainsNoEmbeddedCredentialsOrCreationShortcuts()
    {
        var sourceFiles = ReadSourceFiles();
        var persistenceSource = string.Join(
            Environment.NewLine,
            sourceFiles
                .Where(file =>
                    file.Path.Contains(
                        "WindowsScriptRunner.Infrastructure",
                        StringComparison.Ordinal) ||
                    file.Path.Contains("WindowsScriptRunner.Web", StringComparison.Ordinal) ||
                    file.Path.Contains("WindowsScriptRunner.Worker", StringComparison.Ordinal))
                .Select(file => file.Content));
        string[] credentialPatterns =
        [
            "Password=",
            "Pwd=",
            "User ID=",
            "UID=",
        ];
        string[] creationShortcuts =
        [
            "EnsureCreated",
            "EnsureDeleted",
            ".HasData(",
        ];

        Assert.DoesNotContain(
            credentialPatterns,
            pattern => persistenceSource.Contains(
                pattern,
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            sourceFiles,
            file => creationShortcuts.Any(pattern =>
                file.Content.Contains(pattern, StringComparison.Ordinal)));
    }

    [Fact]
    public void SensitiveDataLoggingIsExplicitlyDisabled()
    {
        var infrastructureSource = ReadProjectSource("WindowsScriptRunner.Infrastructure");

        Assert.Contains(
            "EnableSensitiveDataLogging(false)",
            infrastructureSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EnableSensitiveDataLogging(true)",
            infrastructureSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SaveChangesExistsOnlyInSqlUnitOfWork()
    {
        var files = ReadSourceFiles()
            .Where(file =>
                file.Content.Contains("SaveChanges", StringComparison.Ordinal))
            .ToArray();

        var file = Assert.Single(files);
        Assert.Equal("SqlUnitOfWork.cs", Path.GetFileName(file.Path));
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
        var credentialReferenceId = CredentialReferenceId.New().ToString();
        var parameter = new JobParameter(
            "Credential",
            credentialReferenceId);
        var credential = new CredentialReference(
            CredentialReferenceId.New(),
            "ExternalVault",
            "externalvault://vault/private/path",
            "Automation credential",
            DateTimeOffset.UtcNow,
            new UserIdentity("DOMAIN\\user"));

        Assert.DoesNotContain(credentialReferenceId, parameter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("externalvault://vault/private/path", credential.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JobParameterDoesNotExposeSecurityMetadataConstructor()
    {
        var constructors = typeof(JobParameter).GetConstructors();

        Assert.DoesNotContain(
            constructors,
            constructor => constructor.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(bool) ||
                parameter.ParameterType == typeof(ScriptParameterType)));
    }

    [Fact]
    public void GetJobHandlerDependsOnTrustedScriptRepository()
    {
        var constructor = Assert.Single(typeof(GetJobHandler).GetConstructors());
        var parameterTypes = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IJobRepository), parameterTypes);
        Assert.Contains(typeof(IScriptDefinitionRepository), parameterTypes);
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

    private static IReadOnlyCollection<string> ReadProjectReferences(string projectName)
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", projectName, $"{projectName}.csproj");
        var document = XDocument.Load(projectPath);

        return document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(
                include!.Replace('\\', Path.DirectorySeparatorChar)))
            .ToArray();
    }

    private static IReadOnlyCollection<string> ReadPackageReferences(string projectName)
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", projectName, $"{projectName}.csproj");
        var document = XDocument.Load(projectPath);

        return document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();
    }

    private static IReadOnlyCollection<(string ProjectName, string PackageName)> ReadSourcePackageReferences()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");

        return Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(projectPath =>
            {
                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                var document = XDocument.Load(projectPath);
                return document.Descendants("PackageReference")
                    .Select(element => element.Attribute("Include")?.Value)
                    .Where(include => !string.IsNullOrWhiteSpace(include))
                    .Select(include => (projectName, include!));
            })
            .ToArray();
    }

    private static string ReadProjectSource(string projectName)
    {
        var root = FindRepositoryRoot();
        var projectDirectory = Path.Combine(root, "src", projectName);

        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                    !path.Contains(
                        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal) &&
                    !path.Contains(
                        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                .Select(File.ReadAllText));
    }

    private static IReadOnlyCollection<(string Path, string Content)> ReadSourceFiles()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        return Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
                (Path.GetExtension(path) is ".cs" or ".json" or ".csproj") &&
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) &&
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .Select(path => (path, File.ReadAllText(path)))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WindowsScriptRunner.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from the test execution directory.");
    }
}
