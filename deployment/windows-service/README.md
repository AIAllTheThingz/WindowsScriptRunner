# Windows Service deployment

## Status

`WindowsScriptRunner.Worker` is buildable and contains durable registration, heartbeat, queue polling, lease renewal/recovery, and the disabled-by-default reviewed automation composition. Windows Service packaging and installation are planned for Phase 9 and are not implemented.

This directory does not yet provide:

- `Microsoft.Extensions.Hosting.WindowsServices` composition and service metadata;
- install, upgrade, rollback, start, stop, or uninstall scripts;
- recovery-action configuration;
- a dedicated service identity and least-privilege ACL plan;
- event-log or telemetry-export configuration;
- production configuration and secret injection;
- trusted-script and working-directory creation with reviewed permissions; or
- service health and deployment-verification checks.

Running the Worker interactively is suitable for development validation only. Phase 9 must define the complete Windows Service contract and test clean install, restart, upgrade, rollback, and removal on a representative server.

See [Worker queue](../../docs/worker-queue.md) and [Worker project](../../src/WindowsScriptRunner.Worker/README.md).
