[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$inventory = [ordered]@{
    schemaVersion = '1.0'
    computerName = [System.Environment]::MachineName
    os = [ordered]@{
        description = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        version = [System.Environment]::OSVersion.Version.ToString()
        architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    }
    powerShell = [ordered]@{
        version = $PSVersionTable.PSVersion.ToString()
    }
    collectedUtc = [DateTimeOffset]::UtcNow.ToString(
        'O',
        [System.Globalization.CultureInfo]::InvariantCulture)
}

$inventory | ConvertTo-Json -Depth 3 -Compress
