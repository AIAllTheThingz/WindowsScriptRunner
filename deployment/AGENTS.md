# Deployment Scope Instructions

These instructions apply under `deployment/` in addition to the repository root `AGENTS.md`.

- Deployment work is privileged operational automation and must remain separate from a claim that the application is production-ready.
- Require an explicit target environment, service identity, SQL boundary, Windows/IIS configuration, certificate/SPN/Kerberos assumptions, rollback/recovery plan, and operator authorization before consequential execution.
- Preserve secrets outside source, generated artifacts, command lines, and logs.
- Run prerequisite discovery and validation phases before installation, migration, service, firewall, certificate, or configuration mutation.
- Preserve idempotence and safe rerun behavior where practical; document one-way operations explicitly.
- Record Windows-only validation as `NotRun` or `Blocked` when the available runner is non-Windows.
- Do not infer successful production deployment from repository build/test evidence or from a standards composition result.
