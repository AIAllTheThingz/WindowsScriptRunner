# Development setup

1. Install Git, a stable .NET 10 SDK, and PowerShell 7.
2. Open PowerShell in the repository root.
3. Run `dotnet restore`.
4. Run `dotnet build --configuration Release`.
5. Run `dotnet test --configuration Release`.
6. Run `dotnet format --verify-no-changes`.

Start the web scaffold with:

```powershell
dotnet run --project .\src\WindowsScriptRunner.Web\WindowsScriptRunner.Web.csproj
```

Start the worker scaffold with:

```powershell
dotnet run --project .\src\WindowsScriptRunner.Worker\WindowsScriptRunner.Worker.csproj
```

Development settings contain no secrets. The worker heartbeat defaults to 30 seconds and must remain between 1 and 3600 seconds.
