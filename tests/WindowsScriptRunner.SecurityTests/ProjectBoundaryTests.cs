using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using WindowsScriptRunner.Application.Abstractions;
using WindowsScriptRunner.Application.Jobs;
using WindowsScriptRunner.Application.Queue;
using WindowsScriptRunner.Application.Reports;
using WindowsScriptRunner.Automation;
using WindowsScriptRunner.Domain;
using WindowsScriptRunner.Domain.Credentials;
using WindowsScriptRunner.Domain.Exceptions;
using WindowsScriptRunner.Domain.Identifiers;
using WindowsScriptRunner.Domain.Jobs;
using WindowsScriptRunner.Domain.Scripts;
using WindowsScriptRunner.Domain.ValueObjects;
using WindowsScriptRunner.PowerShell;
using WindowsScriptRunner.Reporting;

namespace WindowsScriptRunner.SecurityTests;

public sealed class ProjectBoundaryTests
{
    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        { "WindowsScriptRunner.Domain", [] },
        { "WindowsScriptRunner.Contracts", [] },
        {
            "WindowsScriptRunner.Application",
            [
                "WindowsScriptRunner.Contracts",
                "WindowsScriptRunner.Domain",
                "WindowsScriptRunner.Reporting",
            ]
        },
        {
            "WindowsScriptRunner.Automation",
            [
                "WindowsScriptRunner.Application",
                "WindowsScriptRunner.Domain",
                "WindowsScriptRunner.PowerShell",
                "WindowsScriptRunner.Reporting",
            ]
        },
        {
            "WindowsScriptRunner.Infrastructure",
            [
                "WindowsScriptRunner.Application",
                "WindowsScriptRunner.Domain",
            ]
        },
        { "WindowsScriptRunner.PowerShell", [] },
        { "WindowsScriptRunner.Reporting", [] },
        {
            "WindowsScriptRunner.Web",
            [
                "WindowsScriptRunner.Application",
                "WindowsScriptRunner.Contracts",
                "WindowsScriptRunner.Infrastructure",
            ]
        },
        {
            "WindowsScriptRunner.Worker",
            [
                "WindowsScriptRunner.Application",
                "WindowsScriptRunner.Automation",
                "WindowsScriptRunner.Contracts",
                "WindowsScriptRunner.Domain",
                "WindowsScriptRunner.Infrastructure",
            ]
        },
    };

    public static TheoryData<string, string> ForbiddenReferences => new()
    {
        { "WindowsScriptRunner.Web", "WindowsScriptRunner.Automation" },
        { "WindowsScriptRunner.Web", "WindowsScriptRunner.PowerShell" },
        { "WindowsScriptRunner.Infrastructure", "WindowsScriptRunner.PowerShell" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Infrastructure" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Web" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Worker" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.PowerShell" },
        { "WindowsScriptRunner.Domain", "WindowsScriptRunner.Reporting" },
        { "WindowsScriptRunner.Reporting", "WindowsScriptRunner.PowerShell" },
        { "WindowsScriptRunner.Reporting", "WindowsScriptRunner.Infrastructure" },
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
        Assert.DoesNotContain("WindowsScriptRunner.Automation", references);
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
    public void WorkerContainsNoPowerShellOrProcessExecutionSurface()
    {
        var source = ReadProjectSource("WindowsScriptRunner.Worker");

        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Diagnostics.Process", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Management.Automation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowsScriptRunner.PowerShell", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessApisAreConfinedToPowerShellProject()
    {
        var files = ReadSourceFiles()
            .Where(file =>
                file.Content.Contains("ProcessStartInfo", StringComparison.Ordinal) ||
                file.Content.Contains("System.Diagnostics.Process", StringComparison.Ordinal) ||
                file.Content.Contains("new Process", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(files);
        Assert.All(
            files,
            file => Assert.Contains(
                "WindowsScriptRunner.PowerShell",
                file.Path,
                StringComparison.Ordinal));
    }

    [Fact]
    public void NativeInteropIsConfinedToPowerShellBoundaryComponents()
    {
        var files = ReadSourceFiles()
            .Where(file =>
                file.Content.Contains("LibraryImport", StringComparison.Ordinal) ||
                file.Content.Contains("DllImport", StringComparison.Ordinal) ||
                file.Content.Contains("JobObject", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            ["ExecutionWorkingDirectory.cs", "ProcessTreeController.cs"],
            files.Select(file => Path.GetFileName(file.Path))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void ProductionProjectsContainNoInProcessPowerShellDependency()
    {
        var packages = ReadSourcePackageReferences();
        var source = string.Join(
            Environment.NewLine,
            ReadSourceFiles().Select(file => file.Content));

        Assert.DoesNotContain(
            packages,
            reference => reference.PackageName == "Microsoft.PowerShell.SDK");
        Assert.DoesNotContain(
            "System.Management.Automation",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Runspace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerShell.Create", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicBoundaryAcceptsOnlyTrustedRequest()
    {
        var execute = Assert.Single(typeof(IPowerShellExecutionBoundary).GetMethods());
        var parameters = execute.GetParameters();

        Assert.Equal(typeof(PowerShellExecutionRequest), parameters[0].ParameterType);
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.ParameterType == typeof(string));
        Assert.Empty(typeof(TrustedPowerShellScript).GetConstructors());
    }

    [Fact]
    public void PowerShellSourceUsesOnlySafeProcessConstruction()
    {
        var source = ReadProjectSource("WindowsScriptRunner.PowerShell");

        Assert.Contains("ArgumentList", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = false", source, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardInput = false", source, StringComparison.Ordinal);
        Assert.Contains("Environment.Clear()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo.Arguments", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("powershell.exe", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cmd.exe", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-Expression", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-ExecutionPolicy", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-EncodedCommand", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetEnvironmentVariables", source, StringComparison.Ordinal);
        Assert.Equal(
            1,
            source.Split("\"-Command\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void WebAndWorkerDoNotRegisterOrReferenceBoundary()
    {
        var webSource = ReadProjectSource("WindowsScriptRunner.Web");
        var workerSource = ReadProjectSource("WindowsScriptRunner.Worker");

        Assert.DoesNotContain(
            "AddPowerShellExecutionBoundary",
            webSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddPowerShellExecutionBoundary",
            workerSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IPowerShellExecutionBoundary",
            workerSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "WindowsScriptRunner.PowerShell",
            ReadProjectReferences("WindowsScriptRunner.Worker"));
    }

    [Theory]
    [InlineData(typeof(JobQueueCandidate))]
    [InlineData(typeof(ClaimedJobWork))]
    public void QueueDescriptorsExposeNoParametersOrCredentials(Type descriptorType)
    {
        string[] prohibitedFragments =
        [
            "Parameter",
            "SerializedValue",
            "CredentialReference",
            "CredentialId",
        ];
        var properties = descriptorType.GetProperties();

        Assert.DoesNotContain(
            properties,
            property => prohibitedFragments.Any(fragment =>
                property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void JobLeaseContainsCoordinationMetadataButNoSecretMaterial()
    {
        var properties = typeof(JobLease).GetProperties();
        string[] expected =
        [
            nameof(JobLease.Id),
            nameof(JobLease.WorkerNodeId),
            nameof(JobLease.WorkKind),
            nameof(JobLease.FencingToken),
            nameof(JobLease.AcquiredUtc),
            nameof(JobLease.LastRenewedUtc),
            nameof(JobLease.ExpiresUtc),
        ];
        string[] prohibited = ["Password", "Secret", "CredentialValue", "Parameter"];

        Assert.All(
            expected,
            name => Assert.Contains(properties, property => property.Name == name));
        Assert.DoesNotContain(
            properties,
            property => prohibited.Any(fragment =>
                property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void WorkerControlledLeaseCommandsRequireFencedCredentials()
    {
        Type[] commandTypes =
        [
            typeof(RenewJobLeaseCommand),
            typeof(ReleaseUnstartedJobLeaseCommand),
            typeof(InspectJobLeaseQuery),
            typeof(StartLeasedDryRunCommand),
            typeof(CompleteLeasedDryRunCommand),
            typeof(CompleteLeasedReadOnlyDryRunCommand),
            typeof(TerminateLeasedDryRunCommand),
            typeof(StartLeasedExecutionCommand),
            typeof(BeginLeasedPostValidationCommand),
            typeof(RecordLeasedExecutionOutcomeCommand),
        ];

        Assert.All(
            commandTypes,
            type => Assert.Contains(
                Assert.Single(type.GetConstructors()).GetParameters(),
                parameter => parameter.ParameterType == typeof(JobLeaseCredentials)));
    }

    [Fact]
    public void ProductionWorkerRegistersNoFakeOrExecutableHandler()
    {
        var source = ReadProjectSource("WindowsScriptRunner.Worker");

        Assert.DoesNotContain(": IJobWorkHandler", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddSingleton<IJobWorkHandler",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddTransient<IJobWorkHandler",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddScoped<IJobWorkHandler",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LeaseAuditSourceDoesNotReadJobParameters()
    {
        var root = FindRepositoryRoot();
        var queueHandlers = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WindowsScriptRunner.Application",
                "Queue",
                "QueueHandlers.cs"));

        Assert.DoesNotContain("SerializedValue", queueHandlers, StringComparison.Ordinal);
        Assert.DoesNotContain("JobParameter", queueHandlers, StringComparison.Ordinal);
        Assert.DoesNotContain("CredentialReference", queueHandlers, StringComparison.Ordinal);
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

    [Fact]
    public void AutomationConfigurationExposesOnlyEnablementFlags()
    {
        var properties = typeof(LocalHostInventoryPackageOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.Equal(
            [
                nameof(LocalHostInventoryPackageOptions.Enabled),
                nameof(LocalHostInventoryPackageOptions.RegisterOnStartup),
            ],
            properties.Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, property => Assert.Equal(typeof(bool), property.PropertyType));
    }

    [Fact]
    public void ReviewedProductionArtifactMatchesPinnedHashAndIsTheOnlyProductionScript()
    {
        var root = FindRepositoryRoot();
        var automationRoot = Path.Combine(
            root,
            "src",
            "WindowsScriptRunner.Automation");
        var script = Assert.Single(
            Directory.EnumerateFiles(
                automationRoot,
                "*.ps1",
                SearchOption.AllDirectories),
            path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) &&
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal));
        var actualHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(script)));

        Assert.Equal(
            "B85B29BBFC04DFB9C85F3FCC391E58C1EA0EF8AEEDDCB5B796D8968B3729C368",
            actualHash);
    }

    [Fact]
    public void AutomationDoesNotConstructProcessesOrConsumeMutableArtifactMetadata()
    {
        var source = ReadProjectSource("WindowsScriptRunner.Automation");
        var optionsProperties = typeof(LocalHostInventoryPackageOptions)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("ProcessStartInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new TrustedPowerShellScript", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            optionsProperties,
            property => property.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                property.Contains("Hash", StringComparison.OrdinalIgnoreCase) ||
                property.Contains("Parameter", StringComparison.OrdinalIgnoreCase) ||
                property.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
                property.Contains("Script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AutomationLoggingAndAuditingDoNotConsumeOutputArgumentsOrInventory()
    {
        var automationRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WindowsScriptRunner.Automation");
        var source = string.Join(
            Environment.NewLine,
            new[]
            {
                "LocalHostInventoryJobWorkHandler.cs",
                "LocalHostInventoryPackageRegistrar.cs",
                "LocalHostInventoryPackageStartupService.cs",
                "LocalHostInventoryResultMapper.cs",
            }.Select(file => File.ReadAllText(Path.Combine(automationRoot, file))));

        var handler = File.ReadAllText(
            Path.Combine(
                automationRoot,
                "LocalHostInventoryJobWorkHandler.cs"));
        Assert.DoesNotContain("ILogger", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("IAuditWriter", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("SerializedValue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.MachineName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEnvironmentVariables", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportWriteBoundaryAcceptsOnlyTheValidatedPackageSpecificType()
    {
        var commandProperties =
            typeof(CompleteLocalHostInventoryDryRunCommand).GetProperties();
        var processProperties =
            typeof(LocalHostInventoryProcessResult).GetProperties();
        var webSource = ReadProjectSource("WindowsScriptRunner.Web");

        Assert.Contains(
            commandProperties,
            property =>
                property.PropertyType ==
                typeof(ValidatedLocalHostInventoryReport));
        Assert.DoesNotContain(
            commandProperties,
            property =>
                property.PropertyType == typeof(string) ||
                property.Name.Contains("Schema", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("ReportType", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Sensitivity", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Risk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            processProperties,
            property =>
                property.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Schema", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("ReportType", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            nameof(GetLocalHostInventoryReportHandler),
            webSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "LocalHostInventoryReportResponse",
            webSource,
            StringComparison.Ordinal);
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
