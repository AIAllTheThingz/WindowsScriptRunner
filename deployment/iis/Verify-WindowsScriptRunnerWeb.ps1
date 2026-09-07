[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishRoot,

    [Parameter(Mandatory)]
    [string]$CertificateThumbprint,

    [string]$SiteName = 'WindowsScriptRunner',
    [string]$AppPoolName = 'WindowsScriptRunner',
    [string]$HostName = 'windows-script-runner.local',
    [int]$Port = 443,
    [switch]$ProbeReadiness,

    [ValidateRange(1, 300)]
    [int]$ReadinessTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\common\DeploymentAssertions.ps1')

Assert-WindowsDeploymentHost
Assert-NativeWindowsPowerShell51
Assert-IisTargetName $SiteName 'SiteName'
Assert-IisTargetName $AppPoolName 'AppPoolName'
Assert-IisHostName $HostName
$resolvedPublishRoot = Resolve-DeploymentAbsolutePath $PublishRoot 'PublishRoot'
Assert-DeploymentDirectory $resolvedPublishRoot 'PublishRoot'
Assert-DeploymentFile (Join-Path $resolvedPublishRoot 'web.config') 'ASP.NET Core web.config'

if ($Port -notin 1..65535) {
    throw 'Port must be between 1 and 65535.'
}
$null = Get-DeploymentCertificate $CertificateThumbprint
Import-Module WebAdministration -ErrorAction Stop
Assert-IisDeploymentEnvironment

$site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
if ($null -eq $site) {
    throw "IIS site '$SiteName' is not installed."
}
$configuredPhysicalPath = ([IO.Path]::GetFullPath([string]$site.PhysicalPath)).TrimEnd('\')
$expectedPhysicalPath = $resolvedPublishRoot.TrimEnd('\')
if (-not $configuredPhysicalPath.Equals($expectedPhysicalPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "IIS site '$SiteName' does not point to the expected PublishRoot."
}
$configuredAppPool = [string]$site.ApplicationPool
if (-not $configuredAppPool.Equals($AppPoolName, [StringComparison]::OrdinalIgnoreCase)) {
    throw "IIS site '$SiteName' does not use the expected application pool."
}
if ($site.State -ne 'Started') {
    throw "IIS site '$SiteName' is not started."
}
$appPool = Get-WebAppPoolState -Name $AppPoolName
if ($appPool.Value -ne 'Started') {
    throw "IIS application pool '$AppPoolName' is not started."
}

$bindingInformation = '*:{0}:{1}' -f $Port, $HostName
$matchingBindings = @(
    Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue |
        Where-Object {
            ([string]$_.bindingInformation).Equals(
                $bindingInformation,
                [StringComparison]::OrdinalIgnoreCase)
        }
)
if ($matchingBindings.Count -ne 1) {
    throw "IIS site '$SiteName' does not have exactly one matching HTTPS binding."
}
if ([int]$matchingBindings[0].sslFlags -ne 1) {
    throw "IIS site '$SiteName' HTTPS binding does not require SNI."
}

$sslBindingPath = "IIS:\SslBindings\0.0.0.0!$Port!$HostName"
$sslBinding = Get-Item -LiteralPath $sslBindingPath -ErrorAction SilentlyContinue
if ($null -eq $sslBinding) {
    throw "IIS has no certificate binding for '$HostName' on port $Port."
}
if (-not ([string]$sslBinding.Thumbprint).Equals(
        $CertificateThumbprint,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'IIS certificate binding does not match the expected certificate.'
}

$windowsAuthentication = Get-WebConfigurationProperty `
    -PSPath "IIS:\Sites\$SiteName" `
    -Filter '/system.webServer/security/authentication/windowsAuthentication' `
    -Name enabled
if ($null -eq $windowsAuthentication -or
    -not ([string]$windowsAuthentication.Value).Equals('True', [StringComparison]::OrdinalIgnoreCase)) {
    throw "IIS site '$SiteName' does not have Windows Authentication enabled."
}

if ($ProbeReadiness) {
    $uri = "https://$HostName`:$Port/health/ready"
    $response = Invoke-WebRequest `
        -Uri $uri `
        -UseBasicParsing `
        -TimeoutSec $ReadinessTimeoutSeconds `
        -ErrorAction Stop
    if ($response.StatusCode -ne 200) {
        throw "Readiness probe returned HTTP $($response.StatusCode)."
    }
}

[pscustomobject]@{
    SiteName = $site.Name
    SiteState = $site.State
    AppPoolName = $AppPoolName
    AppPoolState = $appPool.Value
    HttpsBinding = "https://$HostName`:$Port"
    CertificateThumbprint = $CertificateThumbprint.ToUpperInvariant()
    WindowsAuthenticationEnabled = $true
    ReadinessProbed = [bool]$ProbeReadiness
    ReadinessTimeoutSeconds = if ($ProbeReadiness) { $ReadinessTimeoutSeconds } else { $null }
}
