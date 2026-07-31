[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [string]$ServerInstance,

    [Parameter(Mandatory)]
    [string]$Database,

    [Parameter(Mandatory)]
    [string]$SqlScriptPath,

    [Parameter(Mandatory)]
    [string]$BackupPath,

    [string]$SqlCmdPath = 'sqlcmd.exe',
    [switch]$OverwriteBackup
)

Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\common\DeploymentAssertions.ps1')

Assert-WindowsDeploymentHost
$resolvedSqlScriptPath = Resolve-DeploymentAbsolutePath $SqlScriptPath 'SqlScriptPath'
$resolvedBackupPath = Resolve-DeploymentAbsolutePath $BackupPath 'BackupPath'
Assert-DeploymentFile $resolvedSqlScriptPath 'SQL migration script'

if ([string]::IsNullOrWhiteSpace($ServerInstance) -or $ServerInstance -match '[\r\n]') {
    throw 'ServerInstance must be a non-empty server/instance name without control characters.'
}
$serverInstancePattern = '^(?<host>[^\s\\,]+)(?:(?<instance>\\[^\s\\,]+)|(?<port>,[0-9]{1,5}))?$'
if ($ServerInstance -notmatch $serverInstancePattern) {
    throw 'ServerInstance must be a local SQL Server host with an optional instance or port suffix.'
}
$serverHost = $Matches['host']
$localServerHosts = @('.', '(local)', 'localhost', '127.0.0.1', '::1', [Environment]::MachineName)
if ($serverHost -notin $localServerHosts) {
    throw "ServerInstance '$ServerInstance' is remote. This rollout script supports only local SQL Server topologies."
}
if ($Matches['port']) {
    $serverPort = [int]$Matches['port'].Substring(1)
    if ($serverPort -notin 1..65535) {
        throw 'ServerInstance port must be between 1 and 65535.'
    }
}
if ([string]::IsNullOrWhiteSpace($Database) -or $Database -notmatch '^[A-Za-z0-9_][A-Za-z0-9_$-]{0,127}$') {
    throw 'Database must be a simple SQL identifier without quoting or control characters.'
}
if ((Test-Path -LiteralPath $resolvedBackupPath) -and -not $OverwriteBackup) {
    throw "BackupPath already exists. Choose a new path or explicitly pass -OverwriteBackup: $resolvedBackupPath"
}
$backupDirectory = Split-Path -Parent $resolvedBackupPath
Assert-DeploymentDirectory $backupDirectory 'BackupPath parent directory'

$sqlcmd = Get-Command $SqlCmdPath -ErrorAction SilentlyContinue
if ($null -eq $sqlcmd) {
    throw "The SQL Server command-line tool was not found: $SqlCmdPath"
}

$quotedDatabase = $Database.Replace(']', ']]')
$quotedBackupPath = $resolvedBackupPath.Replace("'", "''")
$backupQuery = "BACKUP DATABASE [$quotedDatabase] TO DISK = N'$quotedBackupPath' WITH COPY_ONLY, INIT, CHECKSUM;"
$backupCreated = $false
$migrationApplied = $false

if ($PSCmdlet.ShouldProcess("$ServerInstance/$Database", "Create COPY_ONLY SQL backup at $resolvedBackupPath")) {
    Invoke-DeploymentNativeCommand $sqlcmd.Source @(
        '-S', $ServerInstance,
        '-d', 'master',
        '-E',
        '-b',
        '-Q', $backupQuery
    )
    $backupCreated = $true
}

if ($PSCmdlet.ShouldProcess("$ServerInstance/$Database", "Apply reviewed idempotent migration $resolvedSqlScriptPath")) {
    if (-not $backupCreated -and -not $WhatIfPreference) {
        throw 'Migration cannot run until the backup completes successfully.'
    }
    Invoke-DeploymentNativeCommand $sqlcmd.Source @(
        '-S', $ServerInstance,
        '-d', $Database,
        '-E',
        '-b',
        '-i', $resolvedSqlScriptPath
    )
    $migrationApplied = $true
}

[pscustomobject]@{
    ServerInstance = $ServerInstance
    Database = $Database
    BackupPath = $resolvedBackupPath
    SqlScriptPath = $resolvedSqlScriptPath
    SqlCmdPath = $sqlcmd.Source
    BackupCreated = $backupCreated
    MigrationApplied = $migrationApplied
}
