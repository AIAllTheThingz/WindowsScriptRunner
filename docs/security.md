# Phase 2 security properties

- No raw credential property exists in the domain model; `CredentialReference` stores only an external identifier.
- Sensitive job parameters redact `ToString`, query responses, and audit values.
- Script paths must be relative and reject rooted or traversal forms.
- Script SHA-256 metadata must contain exactly 64 hexadecimal characters.
- Published script versions reject mutation.
- Web has no direct reference to Worker or PowerShell.
- Domain references no solution, ASP.NET Core, Entity Framework Core, or SQL client assembly.
- Requesters cannot self-approve Medium, High, or Critical work. The documented Phase 2 policy permits ReadOnly self-approval.
- Audit properties reject control characters and obvious sensitive key names.

Authentication, authorization, executable signing, trusted hash calculation, SQL security, external credential retrieval, process isolation, and production approval controls are not implemented.
