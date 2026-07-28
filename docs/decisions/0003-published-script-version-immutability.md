# ADR 0003: Published script-version immutability

Status: Accepted

A published `ScriptVersion` cannot accept parameter-definition changes. Script identity, path, hash, supported phases, timeout, report formats, and PowerShell requirement are constructor-only.

Corrections require a new semantic version. This preserves the meaning of submitted jobs and future approval fingerprints.
