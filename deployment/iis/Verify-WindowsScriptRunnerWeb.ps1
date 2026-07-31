[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishRoot,

    [string]$SiteName = 'WindowsScriptRunner',
    [string]$AppPoolName = 'WindowsScriptRunner',
    [string]$HostName = 'windows-script-runner.local',
    [int]$Port = 443,
    [switch]$ProbeReadiness
)

Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\common\DeploymentAssertions.ps1')

Assert-WindowsDeploymentHost
$resolvedPublishRoot = Resolve-DeploymentAbsolutePath $PublishRoot 'PublishRoot'
Assert-DeploymentDirectory $resolvedPublishRoot 'PublishRoot'
Assert-DeploymentFile (Join-Path $resolvedPublishRoot 'web.config') 'ASP.NET Core web.config'

Import-Module WebAdministration
$site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
if ($null -eq $site) {
    throw "IIS site '$SiteName' is not installed."
}
$configuredPhysicalPath = ([IO.Path]::GetFullPath([string]$site.PhysicalPath)).TrimEnd('\')
$expectedPhysicalPath = $resolvedPublishRoot.TrimEnd('\')
if (-not $configuredPhysicalPath.Equals($expectedPhysicalPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "IIS site '$SiteName' points to '$($site.PhysicalPath)' instead of the expected PublishRoot '$resolvedPublishRoot'."
}
$configuredAppPool = [string]$site.ApplicationPool
if (-not $configuredAppPool.Equals($AppPoolName, [StringComparison]::OrdinalIgnoreCase)) {
    throw "IIS site '$SiteName' uses application pool '$configuredAppPool' instead of '$AppPoolName'."
}
if ($site.State -ne 'Started') {
    throw "IIS site '$SiteName' is not started; current state is '$($site.State)'."
}
$appPool = Get-WebAppPoolState -Name $AppPoolName
if ($appPool.Value -ne 'Started') {
    throw "IIS application pool '$AppPoolName' is not started; current state is '$($appPool.Value)'."
}
$binding = Get-WebBinding -Name $SiteName -Protocol https -Port $Port -HostHeader $HostName -ErrorAction SilentlyContinue
if ($null -eq $binding) {
    throw "IIS site '$SiteName' has no matching HTTPS binding."
}

if ($ProbeReadiness) {
    $uri = "https://$HostName`:$Port/health/ready"
    $response = Invoke-WebRequest -Uri $uri -UseBasicParsing
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
    ReadinessProbed = [bool]$ProbeReadiness
}
