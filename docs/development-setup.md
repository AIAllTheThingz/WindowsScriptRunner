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

The merged Phase 7 baseline is 654 passing tests.

## Local database

Web and Worker require `ConnectionStrings:WindowsScriptRunner`. For a disposable LocalDB database, configure the connection string through user secrets or a process environment variable. Do not commit credentials or workstation-specific connection strings.

Apply the reviewed migrations:

```powershell
dotnet tool run dotnet-ef database update --project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj --startup-project .\src\WindowsScriptRunner.Infrastructure\WindowsScriptRunner.Infrastructure.csproj
```

`Persistence:ApplyMigrationsOnStartup` remains `false` unless a controlled development environment explicitly opts in.

## Start Web

```powershell
dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj
```

The development launch profile uses `http://localhost:5093` and `https://localhost:7127`. Available health routes are:

- `/health`
- `/health/live`
- `/health/ready`

Readiness requires SQL connectivity and no pending migrations. The current Razor Pages are an unauthenticated functional shell; reports and approvals are intentionally not exposed.

## Start Worker

```powershell
dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj
```

Configure a stable non-empty `Worker:NodeId`, or explicitly enable the development-only ephemeral identity option. The heartbeat interval defaults to 30 seconds.

The reviewed inventory package and its registration both default to disabled. Enabling them also requires absolute, local, non-overlapping `PowerShellExecution:AllowedScriptRoot` and `WorkingRoot` paths. See [Worker queue](worker-queue.md) for the complete option contract.

## Safety

Development configuration must contain no committed secrets. The reviewed package accepts no arguments or credentials and performs no remoting, network access, or side effects.
