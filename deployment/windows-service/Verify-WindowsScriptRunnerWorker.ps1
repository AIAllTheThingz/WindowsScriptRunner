[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishRoot,

    [string]$ServiceName = 'WindowsScriptRunner.Worker',
    [switch]$RequireRunning
)

Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\common\DeploymentAssertions.ps1')

Assert-WindowsDeploymentHost
$resolvedPublishRoot = Resolve-DeploymentAbsolutePath $PublishRoot 'PublishRoot'
Assert-DeploymentDirectory $resolvedPublishRoot 'PublishRoot'
$workerExecutable = Join-Path $resolvedPublishRoot 'WindowsScriptRunner.Worker.exe'
Assert-DeploymentFile $workerExecutable 'Worker executable'

$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
if ($null -eq $service) {
    throw "Windows Service '$ServiceName' is not installed."
}

$expectedExecutable = ('"{0}"' -f $workerExecutable)
if ($service.PathName -notlike "$expectedExecutable*") {
    throw "Service '$ServiceName' does not point at the expected published executable."
}

if ($service.StartMode -ne 'Auto') {
    throw "Service '$ServiceName' must use automatic startup; found '$($service.StartMode)'."
}

if ($RequireRunning -and $service.State -ne 'Running') {
    throw "Service '$ServiceName' is not running; current state is '$($service.State)'."
}

[pscustomobject]@{
    ServiceName = $service.Name
    State = $service.State
    StartMode = $service.StartMode
    StartName = $service.StartName
    Executable = $workerExecutable
}
