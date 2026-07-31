[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [string]$PublishRoot,

    [Parameter(Mandatory)]
    [string]$InstallRoot,

    [Parameter(Mandatory)]
    [string]$ServiceAccount,

    [switch]$Upgrade
)

Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\common\DeploymentAssertions.ps1')

Assert-WindowsDeploymentHost
if (-not $WhatIfPreference) {
    Assert-DeploymentAdministrator
}

$resolvedPublishRoot = Resolve-DeploymentAbsolutePath $PublishRoot 'PublishRoot'
$resolvedInstallRoot = Resolve-DeploymentAbsolutePath $InstallRoot 'InstallRoot'
Assert-DeploymentDirectory $resolvedPublishRoot 'PublishRoot'

Assert-DeploymentPathsDoNotOverlap `
    $resolvedPublishRoot `
    'PublishRoot' `
    $resolvedInstallRoot `
    'InstallRoot'
if ([string]::IsNullOrWhiteSpace($ServiceAccount) -or $ServiceAccount -match '[\r\n"]') {
    throw 'ServiceAccount must be a non-empty account name without control characters.'
}

$relativeArtifactPath = 'automation\windows.local-host-inventory\1.0.0\Collect-LocalHostInventory.ps1'
$sourceArtifact = Join-Path $resolvedPublishRoot $relativeArtifactPath
Assert-DeploymentFile $sourceArtifact 'Published reviewed PowerShell artifact'

$expectedSha256 = 'b85b29bbfc04dfb9c85f3fcc391e58c1ea0ef8aeeddcb5b796d8968b3729c368'
$sourceSha256 = (Get-FileHash -LiteralPath $sourceArtifact -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sourceSha256 -ne $expectedSha256) {
    throw "Published artifact hash mismatch. Expected $expectedSha256, found $sourceSha256."
}

$destinationArtifact = Join-Path $resolvedInstallRoot $relativeArtifactPath
if ((Test-Path -LiteralPath $destinationArtifact) -and -not $Upgrade) {
    throw "Installed artifact already exists. Re-run with -Upgrade after reviewing the published hash."
}

if ($PSCmdlet.ShouldProcess($destinationArtifact, 'Install reviewed PowerShell artifact')) {
    $destinationDirectory = Split-Path -Parent $destinationArtifact
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    $stagingArtifact = "$destinationArtifact.$([Guid]::NewGuid().ToString('N')).staging"
    try {
        Copy-Item -LiteralPath $sourceArtifact -Destination $stagingArtifact -Force
        $stagingSha256 = (Get-FileHash -LiteralPath $stagingArtifact -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($stagingSha256 -ne $expectedSha256) {
            throw 'Staged artifact hash changed during copy.'
        }
        Move-Item -LiteralPath $stagingArtifact -Destination $destinationArtifact -Force
    }
    finally {
        if (Test-Path -LiteralPath $stagingArtifact) {
            Remove-Item -LiteralPath $stagingArtifact -Force
        }
    }

    $artifactAcl = '{0}:(OI)(CI)(RX)' -f $ServiceAccount
    Invoke-DeploymentNativeCommand 'icacls.exe' @($resolvedInstallRoot, '/grant', $artifactAcl, '/T', '/C')
}

[pscustomobject]@{
    InstallRoot = $resolvedInstallRoot
    ArtifactPath = $destinationArtifact
    Sha256 = $expectedSha256
    ServiceAccount = $ServiceAccount
    Action = if ($Upgrade) { 'Upgrade' } else { 'Install' }
}
