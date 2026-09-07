Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-WindowsDeploymentHost {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'WindowsScriptRunner deployment tooling must run on Windows.'
    }
}

function Assert-NativeWindowsPowerShell51 {
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or
        $PSVersionTable.PSVersion.Major -ne 5 -or
        $PSVersionTable.PSVersion.Minor -ne 1 -or
        -not [Environment]::Is64BitProcess) {
        throw 'IIS deployment tooling must run in native 64-bit Windows PowerShell 5.1.'
    }
}

function Assert-IisTargetName {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Name) -or
        $Name -in @('.', '..') -or
        $Name -notmatch '\A[A-Za-z0-9 ._-]+\z') {
        throw "$Description must contain only letters, digits, spaces, dots, underscores, or hyphens and must not be . or .."
    }
}

function Assert-IisHostName {
    param(
        [Parameter(Mandatory)]
        [string]$HostName
    )

    if ([string]::IsNullOrWhiteSpace($HostName) -or $HostName.Length -gt 253) {
        throw 'HostName must be a DNS hostname.'
    }
    foreach ($label in $HostName.Split('.')) {
        if ($label.Length -eq 0 -or
            $label.Length -gt 63 -or
            $label -notmatch '\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\z') {
            throw 'HostName must be a DNS hostname.'
        }
    }
}

function Assert-DeploymentAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'An elevated Administrator PowerShell session is required.'
    }
}

function Assert-DeploymentTargetMachine {
    param(
        [Parameter(Mandatory)]
        [string]$ExpectedMachineGuid
    )

    $expected = [Guid]::Empty
    if (-not [Guid]::TryParse($ExpectedMachineGuid, [ref]$expected)) {
        throw 'ExpectedMachineGuid must be a valid GUID.'
    }

    $actualValue = Get-ItemPropertyValue `
        -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Cryptography' `
        -Name 'MachineGuid'
    $actual = [Guid]::Empty
    if (-not [Guid]::TryParse([string]$actualValue, [ref]$actual)) {
        throw 'This computer does not expose a valid MachineGuid.'
    }
    if ($actual -ne $expected) {
        throw 'ExpectedMachineGuid does not match this computer.'
    }
}

function Resolve-DeploymentAbsolutePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^[A-Za-z]:[\\/]') {
        throw "$Description must be an absolute local path."
    }

    $resolved = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($resolved)
    if ([string]::IsNullOrWhiteSpace($root) -or
        $resolved.TrimEnd('\').Equals(
            $root.TrimEnd('\'),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description must not be a volume root."
    }

    $current = $resolved
    while ($true) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and
            ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Description must not contain a reparse point."
        }

        $parent = [IO.Directory]::GetParent($current)
        if ($null -eq $parent) {
            break
        }
        $current = $parent.FullName
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

    $null = Resolve-DeploymentAbsolutePath $Path $Description
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

    $null = Resolve-DeploymentAbsolutePath $Path $Description
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description does not exist: $Path"
    }
}

function Get-DeploymentCertificate {
    param(
        [Parameter(Mandatory)]
        [string]$CertificateThumbprint
    )

    if ($CertificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$') {
        throw 'CertificateThumbprint must be a 40-character certificate thumbprint.'
    }

    $certificate = Get-Item `
        -LiteralPath "Cert:\LocalMachine\My\$CertificateThumbprint" `
        -ErrorAction SilentlyContinue
    if ($null -eq $certificate) {
        throw "HTTPS certificate was not found in the LocalMachine\\My store: $CertificateThumbprint"
    }
    if (-not $certificate.HasPrivateKey) {
        throw 'HTTPS certificate must include a private key.'
    }

    $now = [DateTime]::Now
    if ($certificate.NotBefore -gt $now) {
        throw 'HTTPS certificate is not valid yet.'
    }
    if ($certificate.NotAfter -le $now) {
        throw 'HTTPS certificate has expired.'
    }

    return $certificate
}

function Assert-IisDeploymentEnvironment {
    foreach ($moduleName in 'AspNetCoreModuleV2', 'WindowsAuthenticationModule') {
        if ($null -eq (Get-WebGlobalModule -Name $moduleName -ErrorAction SilentlyContinue)) {
            throw "IIS prerequisite '$moduleName' is not installed."
        }
    }

    $runtimeRoot = Join-Path `
        ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) `
        'dotnet\shared\Microsoft.AspNetCore.App'
    $runtime = Get-ChildItem `
        -LiteralPath $runtimeRoot `
        -Directory `
        -ErrorAction SilentlyContinue |
        Where-Object Name -match '^10\.' |
        Select-Object -First 1
    if ($null -eq $runtime) {
        throw 'The ASP.NET Core 10 runtime required by this deployment is not installed.'
    }
}

function Invoke-DeploymentNativeCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$ArgumentList
    )

    $command = Get-Command -Name $FilePath -CommandType Application, ExternalScript -ErrorAction Stop
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $command.Source @ArgumentList *> $null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($exitCode -ne 0) {
        throw "$FilePath failed with exit code $exitCode."
    }
}
