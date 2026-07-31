# Development setup

## Prerequisites

Install:

- Git;
- a stable .NET 10 SDK;
- PowerShell 7.4 or later; and
- SQL Server or SQL Server LocalDB.

PowerShell 7 is required for the real execution tests. SQL Server LocalDB is supported for development and the SQL test suite.

## Restore and validate

From the repository root:

```powershell
dotnet tool restore
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet format --verify-no-changes
dotnet tool run dotnet-ef migrations has-pending-model-changes --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj --configuration Release --no-build
```

The current Phase 8 validation evidence is recorded in [validation report](validation-report.md).

## Local database

Web and Worker require `ConnectionStrings:WindowsScriptRunner`. For a disposable LocalDB database, configure the connection string through user secrets or a process environment variable. Do not commit credentials or workstation-specific connection strings.

Web also requires approved Windows authorization groups outside the `Testing` environment. Use protected environment-specific configuration; these placeholders are intentionally not usable SIDs and must not be copied as production values:

```powershell
$env:ConnectionStrings__WindowsScriptRunner = '<protected SQL Server connection string>'
$env:WindowsAuthorization__OperatorGroupSids__0 = '<operator-group-sid>'
$env:WindowsAuthorization__ReportReaderGroupSids__0 = '<report-reader-group-sid>'
$env:WindowsAuthorization__ApproverGroupSids__0 = '<approver-group-sid>'
$env:WindowsAuthorization__AdministratorGroupSids__0 = '<administrator-group-sid>'
```

Use group SIDs, not user names, display names, or role strings. Startup rejects malformed, duplicate, broad, anonymous, and service-account group SIDs, and requires an administrator group outside tests. See [Windows authentication](windows-authentication.md).

Apply the reviewed migrations:

```powershell
dotnet tool run dotnet-ef database update --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj
```

`Persistence:ApplyMigrationsOnStartup` remains `false` unless a controlled development environment explicitly opts in.

## Start Web

```powershell
dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj
```

The development launch profile uses `http://localhost:5093` and `https://localhost:7127`. Web uses Windows Negotiate and protects portal routes by default. Available anonymous health routes are:

- `/health`
- `/health/live`
- `/health/ready`

Readiness requires SQL connectivity and no pending migrations. The protected portal includes safe sign-out guidance, access-denied behavior, an Administrator-only page, job detail, typed Local Host Inventory list/lookup/detail, and approval queue/review/decision pages. Exact policies and URLs are in [authorization matrix](authorization-matrix.md).

The local launch profile is not an IIS, TLS, Kerberos/SPN, browser-zone, service-account, or production-hosting validation. Test-only synthetic Windows identities exist only in the security-test project; use a real Windows-authenticated browser and approved group SID configuration for local manual checks. Negotiate does not create an application session to sign out; follow host policy to switch or end the Windows/browser session.

## Start Worker

```powershell
dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj
```

Configure a stable non-empty `Worker:NodeId`, or explicitly enable the development-only ephemeral identity option. The heartbeat interval defaults to 30 seconds.

The reviewed inventory package and its registration both default to disabled. Enabling them also requires absolute, local, non-overlapping `PowerShellExecution:AllowedScriptRoot` and `WorkingRoot` paths. See [Worker queue](worker-queue.md) for the complete option contract.

## Safety

Development configuration must contain no committed secrets. The reviewed package accepts no arguments or credentials and performs no remoting, network access, or side effects.
