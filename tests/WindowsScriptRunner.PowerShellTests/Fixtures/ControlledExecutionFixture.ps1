[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet(
        'Echo',
        'Streams',
        'ExitCode',
        'Sleep',
        'SpawnChild',
        'FloodOutput',
        'Environment',
        'WorkingDirectory')]
    [string]$Mode,

    [string]$Message = '',

    [ValidateRange(0, 255)]
    [int]$RequestedExitCode = 0,

    [ValidateRange(0, 30)]
    [int]$SleepSeconds = 0,

    [ValidatePattern('\A[A-Za-z_][A-Za-z0-9_]{0,99}\z')]
    [string]$EnvironmentVariableName = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)

switch ($Mode) {
    'Echo' {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Message)
        Write-Output "ECHO_BASE64=$([Convert]::ToBase64String($bytes))"
    }

    'Streams' {
        for ($index = 0; $index -lt 512; $index++) {
            Write-Output "WSR_STDOUT_$index`_✓"
            [Console]::Error.WriteLine("WSR_STDERR_$index`_✓")
        }

        [Console]::Out.Flush()
        [Console]::Error.Flush()
        [Environment]::Exit($RequestedExitCode)
    }

    'ExitCode' {
        Write-Output "WSR_EXIT_CODE=$RequestedExitCode"
        [Console]::Out.Flush()
        [Environment]::Exit($RequestedExitCode)
    }

    'Sleep' {
        Set-Content -LiteralPath (Join-Path (Get-Location) 'started.marker') -Value $PID
        Write-Output "PARENT_PID=$PID"
        [Console]::Out.Flush()
        Start-Sleep -Seconds $SleepSeconds
    }

    'SpawnChild' {
        $childStart = [Diagnostics.ProcessStartInfo]::new()
        $childStart.FileName = Join-Path $PSHOME 'pwsh.exe'
        $childStart.UseShellExecute = $false
        $childStart.CreateNoWindow = $true
        $childStart.ArgumentList.Add('-NoLogo')
        $childStart.ArgumentList.Add('-NoProfile')
        $childStart.ArgumentList.Add('-NonInteractive')
        $childStart.ArgumentList.Add('-Command')
        $childStart.ArgumentList.Add('Start-Sleep -Seconds 30')
        $child = [Diagnostics.Process]::Start($childStart)
        try {
            Set-Content -LiteralPath (Join-Path (Get-Location) 'started.marker') -Value $PID
            Set-Content -LiteralPath (Join-Path (Get-Location) 'child.marker') -Value $child.Id
            Write-Output "PARENT_PID=$PID"
            Write-Output "CHILD_PID=$($child.Id)"
            [Console]::Out.Flush()
            Start-Sleep -Seconds $SleepSeconds
        }
        finally {
            $child.Dispose()
        }
    }

    'FloodOutput' {
        Write-Output "PARENT_PID=$PID"
        for ($index = 0; $index -lt 200000; $index++) {
            $line = "WSR_FLOOD_$index`_" + ('X' * 240)
            if ($Message -eq 'StdErr') {
                [Console]::Error.WriteLine($line)
            }
            elseif ($Message -eq 'Both') {
                Write-Output $line
                [Console]::Error.WriteLine($line)
            }
            else {
                Write-Output $line
            }
        }
    }

    'Environment' {
        $value = [Environment]::GetEnvironmentVariable($EnvironmentVariableName)
        Write-Output "ENVIRONMENT_PRESENT=$(-not [string]::IsNullOrEmpty($value))"
        Write-Output "SYSTEMROOT_PRESENT=$(-not [string]::IsNullOrEmpty($env:SystemRoot))"
        Write-Output "TEMP_PRESENT=$(-not [string]::IsNullOrEmpty($env:TEMP))"
    }

    'WorkingDirectory' {
        $bytes = [Text.Encoding]::UTF8.GetBytes([Environment]::CurrentDirectory)
        Write-Output "WORKING_DIRECTORY_BASE64=$([Convert]::ToBase64String($bytes))"
    }
}
