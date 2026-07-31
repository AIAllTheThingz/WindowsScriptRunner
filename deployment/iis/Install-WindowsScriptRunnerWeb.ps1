[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [string]$PublishRoot,

    [Parameter(Mandatory)]
    [string]$CertificateThumbprint,

    [string]$SiteName = 'WindowsScriptRunner',
    [string]$AppPoolName = 'WindowsScriptRunner',
    [string]$HostName = 'windows-script-runner.local',
    [int]$Port = 443
)

Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\common\DeploymentAssertions.ps1')

Assert-WindowsDeploymentHost
if (-not $WhatIfPreference) {
    Assert-DeploymentAdministrator
}

$resolvedPublishRoot = Resolve-DeploymentAbsolutePath $PublishRoot 'PublishRoot'
Assert-DeploymentDirectory $resolvedPublishRoot 'PublishRoot'
Assert-DeploymentFile (Join-Path $resolvedPublishRoot 'web.config') 'ASP.NET Core web.config'

if ($Port -notin 1..65535) {
    throw 'Port must be between 1 and 65535.'
}
if ([string]::IsNullOrWhiteSpace($HostName) -or $HostName -match '[\r\n/:]') {
    throw 'HostName must be a non-empty DNS host name without path or control characters.'
}
if ($CertificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$') {
    throw 'CertificateThumbprint must be a 40-character certificate thumbprint.'
}

Import-Module WebAdministration
$certificatePath = "Cert:\LocalMachine\My\$CertificateThumbprint"
if ($null -eq (Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue)) {
    throw "HTTPS certificate was not found in the LocalMachine\\My store: $CertificateThumbprint"
}

if ($PSCmdlet.ShouldProcess($SiteName, "Configure IIS HTTPS site on port $Port")) {
    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
        New-WebAppPool -Name $AppPoolName | Out-Null
    }

    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedPipelineMode -Value 'Integrated'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.loadUserProfile -Value $false
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name failure.rapidFailProtection -Value $true

    if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
        New-Website -Name $SiteName -PhysicalPath $resolvedPublishRoot -ApplicationPool $AppPoolName -Port $Port -HostHeader $HostName -Ssl | Out-Null
    }
    else {
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $resolvedPublishRoot
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    }

    $binding = Get-WebBinding -Name $SiteName -Protocol https -Port $Port -HostHeader $HostName -ErrorAction SilentlyContinue
    if ($null -eq $binding) {
        New-WebBinding -Name $SiteName -Protocol https -Port $Port -HostHeader $HostName -SslFlags 1 | Out-Null
    }

    $sslBindingPath = "IIS:\SslBindings\0.0.0.0!$Port!$HostName"
    if (Test-Path $sslBindingPath) {
        Remove-Item $sslBindingPath -Force
    }
    New-Item $sslBindingPath -Thumbprint $CertificateThumbprint -SSLFlags 1 | Out-Null

    $webAcl = 'IIS AppPool\{0}:(OI)(CI)(RX)' -f $AppPoolName
    Invoke-DeploymentNativeCommand 'icacls.exe' @($resolvedPublishRoot, '/grant', $webAcl, '/T', '/C')
    Start-WebAppPool -Name $AppPoolName
    Start-Website -Name $SiteName
}

[pscustomobject]@{
    SiteName = $SiteName
    AppPoolName = $AppPoolName
    PublishRoot = $resolvedPublishRoot
    HostName = $HostName
    Port = $Port
    CertificateThumbprint = $CertificateThumbprint.ToUpperInvariant()
}
