Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-WindowsDeploymentHost {
    if (-not [OperatingSystem]::IsWindows()) {
        throw 'WindowsScriptRunner deployment tooling must run on Windows.'
    }
}

function Assert-DeploymentAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'An elevated Administrator PowerShell session is required.'
    }
}

function Resolve-DeploymentAbsolutePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path)) {
        throw "$Description must be an absolute local path."
    }

    $resolved = [IO.Path]::GetFullPath($Path)
    if ($resolved.StartsWith('\\', [StringComparison]::Ordinal) -or
        $resolved.StartsWith('\\?\', [StringComparison]::Ordinal)) {
        throw "$Description must not be a UNC or device path."
    }

    return $resolved
}

function Assert-DeploymentPathsDoNotOverlap {
    param(
        [Parameter(Mandatory)]
        [string]$FirstPath,

        [Parameter(Mandatory)]
        [string]$FirstDescription,

        [Parameter(Mandatory)]
        [string]$SecondPath,

        [Parameter(Mandatory)]
        [string]$SecondDescription
    )

    $first = $FirstPath.TrimEnd('\')
    $second = $SecondPath.TrimEnd('\')
    if ($first.Equals($second, [StringComparison]::OrdinalIgnoreCase) -or
        $first.StartsWith("$second\", [StringComparison]::OrdinalIgnoreCase) -or
        $second.StartsWith("$first\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "$FirstDescription and $SecondDescription must not overlap: '$FirstPath' and '$SecondPath'."
    }
}

function Assert-DeploymentDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description does not exist: $Path"
    }
}

function Assert-DeploymentFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description does not exist: $Path"
    }
}

function Invoke-DeploymentNativeCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}
