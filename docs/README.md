# Documentation

This directory documents the implementation validated through Phase 7. Historical phase evidence remains in `validation-report.md`; current-state documents describe the repository as it exists now.

## Start here

- [Roadmap](roadmap.md) — completed phases, next phase, and production-readiness sequence
- [Implementation roadmap](implementation-roadmap.md) — technical scope by phase
- [Development setup](development-setup.md) — prerequisites, validation, local database, Web, and Worker startup
- [Architecture](architecture.md) — project boundaries and runtime flow
- [Security](security.md) — trust boundaries, protected data, and residual risks
- [Validation report](validation-report.md) — chronological command and test evidence

## Domain and application

- [Domain model](domain-model.md)
- [Job lifecycle](job-lifecycle.md)
- [Application contracts](application-contracts.md)

## Persistence and reporting

- [SQL Server persistence](sql-server-persistence.md)
- [Database schema](database-schema.md)
- [Database migrations](database-migrations.md)

## Worker and execution

- [Worker queue](worker-queue.md)
- [Worker leases](worker-leases.md)
- [PowerShell execution boundary](powershell-execution-boundary.md)

## Architecture decisions

1. [Strongly typed identifiers](decisions/0001-strongly-typed-identifiers.md)
2. [Job state machine](decisions/0002-job-state-machine.md)
3. [Published script-version immutability](decisions/0003-published-script-version-immutability.md)
4. [SQL Server persistence](decisions/0004-sql-server-persistence.md)
5. [Worker queue leasing](decisions/0005-worker-queue-leasing.md)
6. [PowerShell child-process boundary](decisions/0006-powershell-child-process-boundary.md)
7. [First production automation package](decisions/0007-first-production-automation-package.md)
8. [Typed durable inventory reporting](decisions/0008-typed-durable-inventory-reporting.md)

## Deployment status

The `deployment` directory records the Phase 9 boundary. The repository contains buildable Web and Worker projects and reviewed EF migrations, but it does not yet contain production IIS configuration, Windows Service installation, SQL rollout automation, or PowerShell artifact installation tooling.
