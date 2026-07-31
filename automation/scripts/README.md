# Repository-maintenance automation

No repository-maintenance scripts are currently provided in this directory.

Production automation artifacts do not belong here. The reviewed `windows.local-host-inventory` version `1.0.0` artifact is owned by `src/WindowsScriptRunner.Automation/Artifacts/windows.local-host-inventory/1.0.0` so its source, compile-pinned catalog metadata, SHA-256, build-copy rules, handler, and tests remain in one reviewed project.

Future maintenance scripts must be development or release tooling only. They must not become an alternate application execution path or bypass the Automation and PowerShell trust boundaries.
