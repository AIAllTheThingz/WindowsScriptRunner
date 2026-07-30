# Web

Web is the ASP.NET Core Razor Pages composition root. It registers Infrastructure, serves static assets and the current portal shell, and exposes:

- `/health` — process liveness
- `/health/live` — process liveness
- `/health/ready` — SQL connectivity and migration readiness

Current navigation includes Dashboard, Jobs, Scripts, Workers, Audit, and Administration. These pages are functional layout scaffolds rather than completed management workflows.

Web does not:

- execute PowerShell;
- reference Worker, Automation, PowerShell, or Reporting;
- expose typed inventory reports;
- provide authentication or authorization;
- provide approval actions; or
- include production IIS configuration.

Phase 8 owns authenticated Web composition, authorization policies, trusted approval workflows, and any authorized report presentation. Phase 9 owns IIS deployment.

See [Development setup](../../docs/development-setup.md), [Architecture](../../docs/architecture.md), and [IIS deployment status](../../deployment/iis/README.md).
