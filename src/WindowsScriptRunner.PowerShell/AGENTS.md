# PowerShell Execution Boundary Instructions

These instructions apply to `src/WindowsScriptRunner.PowerShell/` in addition to the repository root `AGENTS.md`.

- Maintain PowerShell as a bounded out-of-process execution boundary; do not silently replace it with in-process or arbitrary script execution.
- Preserve explicit executable path, argument, working-directory, timeout, cancellation, stdout/stderr, and exit-code handling.
- Never interpolate untrusted input into shell command text when structured argument passing is available.
- Preserve reviewed package identity, version, hash, trusted-root, and path-validation controls before execution.
- Do not disable certificate/TLS verification, execution safety, or filesystem/path validation as a shortcut.
- Treat PowerShell output as untrusted until it is parsed and validated against the expected package/result contract.
- Tests must cover timeout, cancellation, nonzero exit, malformed output, path rejection, hash/trust failures, and cleanup behavior where applicable.
- Windows junction/reparse-point and other Windows-only trust tests require Windows execution evidence. A Linux failure caused by missing `kernel32.dll` is an environment limitation, not a successful test.
- Real PowerShell execution tests must remain isolated from production targets and credentials.
