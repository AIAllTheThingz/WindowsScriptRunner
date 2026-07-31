# IIS deployment

## Status

Phase 9 has added an explicit HTTPS IIS install and verification script. A production installation has not been performed.

`WindowsScriptRunner.Web` is buildable and publishable. The repository now provides:

- `Install-WindowsScriptRunnerWeb.ps1`, which requires a published `web.config`, an existing LocalMachine certificate thumbprint, an HTTPS host name, and an elevated session; and
- `Verify-WindowsScriptRunnerWeb.ps1`, which checks site, application-pool, binding, and optional readiness state.

The production rollout still requires:

- a representative Windows Server test with the .NET Hosting Bundle;
- certificate provisioning and renewal ownership;
- service-account identity and filesystem permission sign-off;
- protected production configuration injection;
- health-probe, Negotiate, SPN/Kerberos, and browser-zone validation; and
- installation, upgrade, rollback, and verification evidence.

The Web project currently provides a Razor Pages shell plus `/health`, `/health/live`, and `/health/ready`. It does not expose reports or approval actions.

Do not treat `dotnet publish` output or a script dry run as a production IIS installation. The HTTPS script requires an existing certificate and the verification script can probe readiness, but this repository change does not claim representative-host validation.
