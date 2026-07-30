# PowerShell execution boundary

## Scope

Phase 5 establishes one secure process-lifetime boundary for PowerShell 7 on Windows. It executes only the copied, hashed `tests/WindowsScriptRunner.PowerShellTests/Fixtures/ControlledExecutionFixture.ps1` artifact during integration tests. It does not select arbitrary scripts, accept command text, retrieve credentials, dispatch production queue work, persist reports, or implement Phase 6.

Out-of-process execution keeps PowerShell engine types and runspaces out of the application and gives the boundary operating-system process handles, separate pipes, exit codes, time limits, and tree termination. Windows PowerShell 5.1 is rejected: only a compatible PowerShell Core `pwsh.exe` is accepted.

## Runtime discovery and probe

Candidates are considered in this order:

1. `PowerShellExecution:ExecutablePath`
2. `WINDOWSSCRIPTRUNNER_PWSH_PATH`
3. `pwsh.exe` files found by directly inspecting PATH entries
4. stable installations beneath `%ProgramFiles%\PowerShell`

Paths are canonicalized and deduplicated case-insensitively. Explicit and environment overrides are authoritative. Otherwise compatible stable candidates are ordered by highest version and then path for deterministic selection; stable builds are preferred to previews. The default minimum is 7.4.0. Previews are disabled unless configured, and 64-bit is required by default.

The probe uses only `-NoLogo -NoProfile -NonInteractive -Command <constant probe>`. The command is compile-time controlled and contains no caller data. Its bounded JSON result supplies version, PSEdition, platform, OS, and process architecture. `PSEdition=Core` and the Windows platform are mandatory. The first successful runtime is cached so scripts do not cause repeated probes.

## Trusted artifact and arguments

`TrustedPowerShellScript` exposes artifact name, canonical path, expected SHA-256, and allowed parameter names, but its constructor is internal and visible only to the PowerShell tests. Phase 5 has no production artifact resolver.

Immediately before launch the validator requires a fully qualified canonical local `.ps1` path beneath the allowed root. Root containment includes a trailing separator and uses Windows case-insensitive comparison. UNC/device paths, traversal, sibling-prefix escapes, alternate data streams, missing files, directories, wrong extensions, and any symbolic-link/junction/reparse component are rejected. SHA-256 is recomputed and compared in constant time. Closing the hash handle before process startup leaves a small filesystem time-of-check/time-of-use window.

Arguments are named only. Names must match `[A-Za-z_][A-Za-z0-9_]{0,99}`, belong to the artifact allowlist, and be unique case-insensitively. Count and value length are bounded. Null, NUL-containing, and sensitive-classified values fail before script startup. Command-line parameters are OS-inspectable, so Phase 5 accepts no secrets.

Startup uses `ProcessStartInfo.ArgumentList`:

```text
-NoLogo
-NoProfile
-NonInteractive
-File
<trusted canonical path>
-<allowed name>
<literal value>
```

No command-line string, shell, `Invoke-Expression`, encoded caller command, stop-parsing token, `powershell.exe`, or execution-policy bypass is used. The fixture runs under the host's real execution policy.

## Isolation and lifecycle

Each execution creates `<working-root>\<execution-id>` and starts there with `UseShellExecute=false`, redirected UTF-8 stdout/stderr, no redirected stdin, no console window, and the trusted runtime path. The directory is removed after success, nonzero exit, startup/trust failure, timeout, output overflow, or cancellation. Output and script copies are not retained.

The inherited environment is cleared. Only the fixed Windows runtime allowlist is copied, then `POWERSHELL_TELEMETRY_OPTOUT=1` and `POWERSHELL_UPDATECHECK=Off` are set. Arbitrary parent variables, API keys, connection strings, cloud credentials, and test sentinels do not cross the boundary.

Stdout and stderr are drained concurrently from process start with fixed-size buffers. Separate and combined byte limits bound stored content. A limit breach marks truncation, stops further storage while continuing to drain, terminates the tree, and returns `OutputLimitExceeded`. Output text never enters automatic logs.

A normal process exit returns its exact exit code, including nonzero codes. Timeout returns `TimedOut` and does not masquerade as caller cancellation. In-flight caller cancellation stops capture, terminates and drains the tree, cleans the directory, and throws `OperationCanceledException`.

The boundary attempts a Windows Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` immediately after startup. Timeout, cancellation, overflow, and disposal kill descendants. `Process.Kill(entireProcessTree: true)` is the fallback and retry path. Termination has a bounded grace period and fails critically if the root remains. Because the process is not created suspended, a small startup-to-assignment race remains. A Job Object is lifetime containment, not a filesystem, registry, network, language, token, or privilege sandbox.

## Controlled fixture

The single fixture uses strict mode and fixed parameters. Its allowlisted modes are:

- `Echo` — Base64-encodes the exact UTF-8 message without evaluating it.
- `Streams` — writes deterministic Unicode markers concurrently to both streams and can return a bounded nonzero code.
- `ExitCode` — returns the requested bounded code.
- `Sleep` — emits its PID and waits for timeout tests.
- `SpawnChild` — starts one fixed-command child from `$PSHOME\pwsh.exe`, emits both PIDs, and waits for tree-termination tests.
- `FloodOutput` — emits bounded deterministic stdout, stderr, or both until the boundary stops it.
- `Environment` — reports only whether a conservatively named variable exists, never its value.
- `WorkingDirectory` — reports the current directory in Base64.

Injection tests include whitespace, quotes, semicolons, ampersands, pipes, backticks, `$()`, `${}`, wildcards, redirection, Unicode, newlines, empty strings, and a recognizable command marker. Every value round-trips literally; no marker executes.

## Logging and Phase 6 contract

Logs may contain execution ID, artifact name, PowerShell version, timestamps, duration, exit code, termination reason, byte counts, and truncation state. They omit the executable path, parameter values, stdout, stderr, script contents, complete command lines, environment values, connection strings, credential identifiers, and secrets.

Phase 6 may add a separately reviewed production trusted-artifact resolver and leased queue handler. It must preserve the Phase 5 request model, never pass secrets on the command line, and retain fencing/idempotency rules. Phase 5 registers nothing in Web or Worker and changes no queue or persistence behavior.
