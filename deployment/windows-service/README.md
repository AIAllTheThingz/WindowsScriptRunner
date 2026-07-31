# Windows Service deployment

## Status

`WindowsScriptRunner.Worker` is buildable and contains durable registration, heartbeat, queue polling, lease renewal/recovery, and the disabled-by-default reviewed automation composition. Phase 9 adds Windows Service hosting integration and explicit install/verify scripts. A production installation has not been performed.

The repository now provides:

- `Install-WindowsScriptRunnerWorker.ps1` with explicit service identity, publish-root validation, automatic restart actions, ACL setup, upgrade protection, and `-WhatIf` support; and
- `Verify-WindowsScriptRunnerWorker.ps1` for installed path, automatic-start, and optional running-state checks.

The installer accepts only the matching virtual service account (`NT SERVICE\WindowsScriptRunner.Worker`) or a pre-provisioned group Managed Service Account (`DOMAIN\name$`). Ordinary user accounts and built-in service identities are rejected; the selected identity must already have its approved rights before installation.

The production rollout still requires:

- a representative Windows Server installation and clean install/upgrade/rollback rehearsal;
- a dedicated service identity with approved least-privilege rights;
- event-log or telemetry-export configuration;
- protected production configuration and secret injection;
- trusted-script and working-directory creation with reviewed permissions; and
- service health and deployment-verification evidence.

Running the Worker interactively is suitable for development validation only. The installer does not start a service unless `-Start` is supplied, and no deployment or rollback has been executed by this repository change.

See [Worker queue](../../docs/worker-queue.md) and [Worker project](../../src/WindowsScriptRunner.Worker/README.md).
