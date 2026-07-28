# Phase 2 security properties

- No raw credential property exists in the domain model; `CredentialReference` stores only an external identifier.
- Sensitive job parameters redact `ToString`, query responses, and audit values.
- Script paths must be relative and reject rooted or traversal forms.
- Script SHA-256 metadata must contain exactly 64 hexadecimal characters.
- Published script versions reject mutation.
- Web has no direct reference to Worker or PowerShell.
- Domain references no solution, ASP.NET Core, Entity Framework Core, or SQL client assembly.
- Submission captures trusted script risk and Execute-phase capability in an immutable job policy snapshot.
- Requesters cannot self-approve Medium, High, or Critical work, and callers cannot lower risk at approval time. The documented Phase 2 policy permits ReadOnly and Low self-approval.
- Read-only completion requires captured ReadOnly risk and a captured absence of Execute support; callers cannot override either value.
- Approval, rejection, execution, and completion states require dedicated evidence-bearing operations; the generic application transition handler rejects protected targets.
- Aggregate actor, timestamp, lifecycle, and child-object validation occurs before mutation.
- Source project files are parsed to enforce exact project-reference allowlists, including explicit Web-to-Worker and Web-to-PowerShell prohibitions.
- Audit properties reject control characters and obvious sensitive key names.

Authentication, authorization, executable signing, trusted hash calculation, SQL security, external credential retrieval, process isolation, and production approval controls are not implemented.
