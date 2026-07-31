# IIS deployment

## Status

Production IIS deployment is planned for Phase 9 and is not implemented.

`WindowsScriptRunner.Web` is buildable and publishable, but this directory does not yet provide:

- an IIS site or application-pool definition;
- the required .NET Hosting Bundle installation check;
- HTTPS bindings or certificate provisioning;
- service-account identity and filesystem permissions;
- production configuration injection;
- health-probe integration;
- authentication configuration; or
- installation, upgrade, rollback, and verification scripts.

The Web project currently provides a Razor Pages shell plus `/health`, `/health/live`, and `/health/ready`. It does not expose reports or approval actions.

Do not treat `dotnet publish` output alone as a production IIS installation. Phase 9 must define the complete server contract and validate it on a representative Windows Server host.
