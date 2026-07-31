[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [string]$PublishRoot,

    [Parameter(Mandatory)]
    [string]$ServiceAccount,

    [string]$ServiceName = 'WindowsScriptRunner.Worker',
    [string]$DisplayName = 'Windows Script Runner Worker',
    [string]$Description = 'Durable Windows Script Runner queue worker.',
    [switch]$Upgrade,
    [switch]$Start
)

Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\common\DeploymentAssertions.ps1')

Assert-WindowsDeploymentHost
if (-not $WhatIfPreference) {
    Assert-DeploymentAdministrator
}

$resolvedPublishRoot = Resolve-DeploymentAbsolutePath $PublishRoot 'PublishRoot'
Assert-DeploymentDirectory $resolvedPublishRoot 'PublishRoot'
$workerExecutable = Join-Path $resolvedPublishRoot 'WindowsScriptRunner.Worker.exe'
Assert-DeploymentFile $workerExecutable 'Worker executable'

if ([string]::IsNullOrWhiteSpace($ServiceName) -or
    $ServiceName -notmatch '^[A-Za-z0-9_.-]{1,80}$') {
    throw 'ServiceName must contain only letters, digits, dot, underscore, or hyphen.'
}

if ([string]::IsNullOrWhiteSpace($ServiceAccount) -or
    $ServiceAccount -match '[\r\n"]') {
    throw 'ServiceAccount must be a non-empty account name without control characters.'
}
$virtualServiceAccount = "NT SERVICE\$ServiceName"
$isVirtualServiceAccount = $ServiceAccount.Equals($virtualServiceAccount, [StringComparison]::OrdinalIgnoreCase)
$isGmsa = $ServiceAccount -match '^[^\\\s/"]+\\[^\\\s/"]+\$$'
if ($ServiceAccount -in @(
        'LocalSystem',
        'LocalService',
        'NetworkService',
        'NT AUTHORITY\LocalService',
        'NT AUTHORITY\NetworkService'
    ) -or (-not $isVirtualServiceAccount -and -not $isGmsa)) {
    throw "ServiceAccount must be the matching virtual account ('$virtualServiceAccount') or a validated gMSA ('DOMAIN\name$')."
}

$existingService = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
if ($null -ne $existingService -and -not $Upgrade) {
    throw "Service '$ServiceName' already exists. Re-run with -Upgrade after reviewing the target executable."
}

$quotedExecutable = '"{0}"' -f $workerExecutable
$serviceAction = if ($null -eq $existingService) { 'Create' } else { 'Update' }

if ($PSCmdlet.ShouldProcess($ServiceName, "$serviceAction Windows Service using $workerExecutable")) {
    if ($null -ne $existingService -and $existingService.State -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
    }

    if ($null -eq $existingService) {
        Invoke-DeploymentNativeCommand 'sc.exe' @(
            'create', $ServiceName,
            "binPath= $quotedExecutable",
            'start= auto',
            "obj= $ServiceAccount",
            'password= ""',
            "DisplayName= $DisplayName"
        )
    }
    else {
        Invoke-DeploymentNativeCommand 'sc.exe' @(
            'config', $ServiceName,
            "binPath= $quotedExecutable",
            'start= auto',
            "obj= $ServiceAccount",
            'password= ""',
            "DisplayName= $DisplayName"
        )
    }

    Invoke-DeploymentNativeCommand 'sc.exe' @('description', $ServiceName, $Description)
    Invoke-DeploymentNativeCommand 'sc.exe' @(
        'failure', $ServiceName,
        'reset= 86400',
        'actions= restart/60000/restart/60000/restart/60000'
    )

    $serviceAcl = '{0}:(OI)(CI)(RX)' -f $ServiceAccount
    Invoke-DeploymentNativeCommand 'icacls.exe' @($resolvedPublishRoot, '/grant', $serviceAcl, '/T', '/C')

    if ($Start) {
        Start-Service -Name $ServiceName
    }
}

[pscustomobject]@{
    ServiceName = $ServiceName
    ServiceAccount = $ServiceAccount
    PublishRoot = $resolvedPublishRoot
    Action = $serviceAction
    Started = [bool]$Start
}
