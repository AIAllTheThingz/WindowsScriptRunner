Describe 'Deployment assertions' {
    BeforeAll {
        $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        . (Join-Path $repositoryRoot 'deployment\common\DeploymentAssertions.ps1')
    }

    It 'rejects volume roots, accepts forward-slash local paths, and rejects reparse ancestors' {
        $ordinary = Join-Path $TestDrive 'ordinary'
        New-Item -ItemType Directory -Path $ordinary | Out-Null

        { Resolve-DeploymentAbsolutePath ([IO.Path]::GetPathRoot($ordinary)) 'volume root' } | Should -Throw '*volume root*'
        $resolved = Resolve-DeploymentAbsolutePath ($ordinary.Replace('\', '/')) 'ordinary directory'
        $resolved.TrimEnd('\') | Should -Be $ordinary.TrimEnd('\')

        $target = Join-Path $TestDrive 'junction-target'
        $junction = Join-Path $TestDrive 'junction'
        New-Item -ItemType Directory -Path $target | Out-Null
        New-Item -ItemType Junction -Path $junction -Target $target | Out-Null
        try {
            { Resolve-DeploymentAbsolutePath (Join-Path $junction 'child') 'junction child' } | Should -Throw '*reparse point*'
        }
        finally {
            Remove-Item -LiteralPath $junction -Force -ErrorAction SilentlyContinue
        }
    }

    It 'allows IIS names with spaces and rejects path or wildcard syntax' {
        { Assert-IisTargetName 'Windows Script Runner' 'SiteName' } | Should -Not -Throw
        { Assert-IisTargetName '..' 'SiteName' } | Should -Throw '*SiteName must*'
        { Assert-IisTargetName 'other\site' 'SiteName' } | Should -Throw '*SiteName must*'
        { Assert-IisTargetName 'other*site' 'SiteName' } | Should -Throw '*SiteName must*'
        { Assert-IisTargetName ('site' + [char]10) 'SiteName' } | Should -Throw '*SiteName must*'
        { Assert-IisHostName 'windows-script-runner.local' } | Should -Not -Throw
        { Assert-IisHostName 'bad host!' } | Should -Throw '*HostName must*'
        { Assert-IisHostName ('host' + [char]10) } | Should -Throw '*HostName must*'
    }

    It 'suppresses native output and retains nonzero exit handling' {
        $engine = (Get-Process -Id $PID).Path
        $successArguments = @('-NoProfile', '-NonInteractive', '-Command', 'Write-Output 123; [Console]::Error.WriteLine(456); exit 0')
        $output = (& { Invoke-DeploymentNativeCommand $engine $successArguments } 2>&1 | Out-String).Trim()
        $output | Should -Be ''

        $failureArguments = @('-NoProfile', '-NonInteractive', '-Command', 'exit 7')
        { Invoke-DeploymentNativeCommand $engine $failureArguments } | Should -Throw '*exit code 7*'

        $malformedExecutable = Join-Path $TestDrive 'malformed.exe'
        [IO.File]::WriteAllBytes($malformedExecutable, [byte[]](0, 1, 2, 3))
        $previousExitCode = $global:LASTEXITCODE
        try {
            $global:LASTEXITCODE = 0
            { Invoke-DeploymentNativeCommand $malformedExecutable @('arg') } | Should -Throw '*failed to run*'
        }
        finally {
            $global:LASTEXITCODE = $previousExitCode
        }
    }
}

Describe 'Worker verification' {
    BeforeAll {
        $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:workerVerifierPath = Join-Path $repositoryRoot 'deployment\windows-service\Verify-WindowsScriptRunnerWorker.ps1'
    }

    BeforeEach {
        $script:workerPublishRoot = Join-Path $TestDrive 'worker-publish'
        New-Item -ItemType Directory -Path $script:workerPublishRoot -Force | Out-Null
        $script:workerExecutable = Join-Path $script:workerPublishRoot 'WindowsScriptRunner.Worker.exe'
        New-Item -ItemType File -Path $script:workerExecutable -Force | Out-Null
        $script:workerAccount = 'NT SERVICE\WindowsScriptRunner.Worker'
    }

    It 'rejects service command-line arguments after the expected executable' {
        Mock Get-CimInstance {
            [pscustomobject]@{
                Name = 'WindowsScriptRunner.Worker'
                PathName = ('"{0}" --unexpected' -f $script:workerExecutable)
                StartMode = 'Auto'
                State = 'Stopped'
                StartName = 'NT SERVICE\WindowsScriptRunner.Worker'
            }
        }

        $parameters = @{
            PublishRoot = $script:workerPublishRoot
            ExpectedServiceAccount = $script:workerAccount
        }
        { & $workerVerifierPath @parameters } | Should -Throw '*expected published executable*'
    }

    It 'rejects a service running under a different account' {
        Mock Get-CimInstance {
            [pscustomobject]@{
                Name = 'WindowsScriptRunner.Worker'
                PathName = ('"{0}"' -f $script:workerExecutable)
                StartMode = 'Auto'
                State = 'Stopped'
                StartName = 'NT SERVICE\AnotherWorker'
            }
        }

        $parameters = @{
            PublishRoot = $script:workerPublishRoot
            ExpectedServiceAccount = $script:workerAccount
        }
        { & $workerVerifierPath @parameters } | Should -Throw '*expected service account*'
    }
}

Describe 'IIS deployment guards' {
    BeforeAll {
        $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        . (Join-Path $repositoryRoot 'deployment\common\DeploymentAssertions.ps1')
        $script:iisInstallerPath = Join-Path $repositoryRoot 'deployment\iis\Install-WindowsScriptRunnerWeb.ps1'
        $script:iisVerifierPath = Join-Path $repositoryRoot 'deployment\iis\Verify-WindowsScriptRunnerWeb.ps1'
        $script:testThumbprint = '0123456789012345678901234567890123456789'
        $script:machineGuid = (Get-ItemPropertyValue -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid).ToString()
        function script:Get-Website {}
        function script:Get-WebApplication {}
        function script:Get-WebAppPoolState {}
        function script:Get-WebBinding {}
        function script:Get-WebConfigurationProperty {}
    }

    It 'rejects hostile names before IIS lookup or mutation' {
        Mock Import-Module {}
        Mock Set-ItemProperty {}
        $parameters = @{
            PublishRoot = $TestDrive
            CertificateThumbprint = $testThumbprint
            ExpectedMachineGuid = $machineGuid
            SiteName = '..\other'
            WhatIf = $true
        }

        { & $iisInstallerPath @parameters } | Should -Throw '*SiteName must*'

        Assert-MockCalled Import-Module -Times 0 -Exactly
        Assert-MockCalled Set-ItemProperty -Times 0 -Exactly
    }

    It 'rejects a shared existing app pool before mutation' {
        $publishRoot = Join-Path $TestDrive 'web-publish'
        New-Item -ItemType Directory -Path $publishRoot | Out-Null
        New-Item -ItemType File -Path (Join-Path $publishRoot 'web.config') | Out-Null

        Mock Get-DeploymentCertificate {
            [pscustomobject]@{
                HasPrivateKey = $true
                NotBefore = [DateTime]::Now.AddDays(-1)
                NotAfter = [DateTime]::Now.AddDays(1)
            }
        }
        Mock Import-Module {}
        Mock Assert-IisDeploymentEnvironment {}
        Mock Test-Path {
            param($LiteralPath, $PathType)

            if ($LiteralPath -eq 'IIS:\AppPools\Target Pool') {
                return $true
            }
            if ($PathType -eq 'Container') {
                return [IO.Directory]::Exists($LiteralPath)
            }
            if ($PathType -eq 'Leaf') {
                return [IO.File]::Exists($LiteralPath)
            }
            return $false
        }
        Mock Get-Website {
            [pscustomobject]@{
                Name = 'Other Site'
                ApplicationPool = 'Target Pool'
            }
        }
        Mock Get-WebBinding { $null }
        Mock Set-ItemProperty {}
        $parameters = @{
            PublishRoot = $publishRoot
            CertificateThumbprint = $testThumbprint
            ExpectedMachineGuid = $machineGuid
            SiteName = 'Target Site'
            AppPoolName = 'Target Pool'
            WhatIf = $true
        }

        { & $iisInstallerPath @parameters } | Should -Throw '*already used by another site or application*'

        Assert-MockCalled Set-ItemProperty -Times 0 -Exactly
    }

    It 'rejects a non-DNS hostname before IIS lookup or mutation' {
        Mock Import-Module {}
        Mock Set-ItemProperty {}
        $parameters = @{
            PublishRoot = $TestDrive
            CertificateThumbprint = $testThumbprint
            ExpectedMachineGuid = $machineGuid
            HostName = 'bad host!'
            WhatIf = $true
        }

        { & $iisInstallerPath @parameters } | Should -Throw '*HostName must*'

        Assert-MockCalled Import-Module -Times 0 -Exactly
        Assert-MockCalled Set-ItemProperty -Times 0 -Exactly
    }

    It 'rejects a host and port bound by another IIS site before mutation' {
        $publishRoot = Join-Path $TestDrive 'tuple-publish'
        New-Item -ItemType Directory -Path $publishRoot | Out-Null
        New-Item -ItemType File -Path (Join-Path $publishRoot 'web.config') | Out-Null

        Mock Get-DeploymentCertificate { [pscustomobject]@{} }
        Mock Import-Module {}
        Mock Assert-IisDeploymentEnvironment {}
        Mock Get-Website {
            [pscustomobject]@{
                Name = 'Other Site'
                ApplicationPool = 'Other Pool'
            }
        }
        Mock Get-WebBinding {
            [pscustomobject]@{
                bindingInformation = '192.0.2.5:443:windows-script-runner.local'
                sslFlags = 1
            }
        }
        Mock Set-ItemProperty {}
        $parameters = @{
            PublishRoot = $publishRoot
            CertificateThumbprint = $testThumbprint
            ExpectedMachineGuid = $machineGuid
            SiteName = 'Target Site'
            AppPoolName = 'Target Pool'
            WhatIf = $true
        }

        { & $iisInstallerPath @parameters } | Should -Throw '*another IIS site already has an HTTPS binding*'

        Assert-MockCalled Set-ItemProperty -Times 0 -Exactly
    }

    It 'allows the target site to retain its own host and port binding' {
        $publishRoot = Join-Path $TestDrive 'own-tuple-publish'
        New-Item -ItemType Directory -Path $publishRoot | Out-Null
        New-Item -ItemType File -Path (Join-Path $publishRoot 'web.config') | Out-Null

        Mock Get-DeploymentCertificate { [pscustomobject]@{} }
        Mock Import-Module {}
        Mock Assert-IisDeploymentEnvironment {}
        Mock Get-Website {
            [pscustomobject]@{
                Name = 'Target Site'
                ApplicationPool = 'Target Pool'
            }
        }
        Mock Test-Path {
            param($LiteralPath, $PathType)

            if ($LiteralPath -like 'IIS:\*') {
                return $false
            }
            if ($PathType -eq 'Container') {
                return [IO.Directory]::Exists($LiteralPath)
            }
            if ($PathType -eq 'Leaf') {
                return [IO.File]::Exists($LiteralPath)
            }
            return $false
        }
        Mock Set-ItemProperty {}
        $parameters = @{
            PublishRoot = $publishRoot
            CertificateThumbprint = $testThumbprint
            ExpectedMachineGuid = $machineGuid
            SiteName = 'Target Site'
            AppPoolName = 'Target Pool'
            WhatIf = $true
        }

        $result = & $iisInstallerPath @parameters

        $result.Status | Should -Be 'WhatIf'
        Assert-MockCalled Set-ItemProperty -Times 0 -Exactly
    }

    It 'rejects HTTPS binding sslFlags <SslFlags>' -ForEach @(
        @{ SslFlags = 0 }
        @{ SslFlags = 2 }
        @{ SslFlags = 3 }
    ) {
        $publishRoot = Join-Path $TestDrive ('verify-publish-{0}' -f $SslFlags)
        New-Item -ItemType Directory -Path $publishRoot | Out-Null
        New-Item -ItemType File -Path (Join-Path $publishRoot 'web.config') | Out-Null

        Mock Get-DeploymentCertificate { [pscustomobject]@{} }
        Mock Import-Module {}
        Mock Assert-IisDeploymentEnvironment {}
        Mock Get-Website {
            [pscustomobject]@{
                Name = 'Windows Script Runner'
                PhysicalPath = $publishRoot
                ApplicationPool = 'Target Pool'
                State = 'Started'
            }
        }
        Mock Get-WebAppPoolState { [pscustomobject]@{ Value = 'Started' } }
        Mock Get-WebBinding {
            [pscustomobject]@{
                bindingInformation = '*:443:windows-script-runner.local'
                sslFlags = $SslFlags
            }
        }
        $parameters = @{
            PublishRoot = $publishRoot
            CertificateThumbprint = $testThumbprint
            SiteName = 'Windows Script Runner'
            AppPoolName = 'Target Pool'
        }

        { & $iisVerifierPath @parameters } | Should -Throw '*does not require SNI*'
    }

    It 'accepts sslFlags 1, pins the certificate, and uses the configured readiness timeout' {
        $publishRoot = Join-Path $TestDrive 'verify-publish-sni'
        New-Item -ItemType Directory -Path $publishRoot | Out-Null
        New-Item -ItemType File -Path (Join-Path $publishRoot 'web.config') | Out-Null

        Mock Get-DeploymentCertificate { [pscustomobject]@{} }
        Mock Import-Module {}
        Mock Assert-IisDeploymentEnvironment {}
        Mock Get-Website {
            [pscustomobject]@{
                Name = 'Windows Script Runner'
                PhysicalPath = $publishRoot
                ApplicationPool = 'Target Pool'
                State = 'Started'
            }
        }
        Mock Get-WebAppPoolState { [pscustomobject]@{ Value = 'Started' } }
        Mock Get-WebBinding {
            [pscustomobject]@{
                bindingInformation = '*:443:windows-script-runner.local'
                sslFlags = 1
            }
        }
        Mock Get-Item {
            param([string[]]$LiteralPath)

            $path = $LiteralPath[0]
            if ($path -like 'IIS:\SslBindings\*') {
                return [pscustomobject]@{ Thumbprint = '0123456789012345678901234567890123456789' }
            }
            if ([IO.Directory]::Exists($path)) {
                return [IO.DirectoryInfo]$path
            }
            if ([IO.File]::Exists($path)) {
                return [IO.FileInfo]$path
            }
        }
        Mock Get-WebConfigurationProperty { [pscustomobject]@{ Value = $true } }
        Mock Invoke-WebRequest { [pscustomobject]@{ StatusCode = 200 } }
        $parameters = @{
            PublishRoot = $publishRoot
            CertificateThumbprint = $testThumbprint
            SiteName = 'Windows Script Runner'
            AppPoolName = 'Target Pool'
            ProbeReadiness = $true
            ReadinessTimeoutSeconds = 7
        }

        $result = & $iisVerifierPath @parameters

        $result.WindowsAuthenticationEnabled | Should -BeTrue
        $result.ReadinessProbed | Should -BeTrue
        Assert-MockCalled Invoke-WebRequest -Times 1 -Exactly -ParameterFilter { $TimeoutSec -eq 7 }
    }

    It 'rejects a certificate binding that does not match the expected pin' {
        $publishRoot = Join-Path $TestDrive 'verify-publish-pin'
        New-Item -ItemType Directory -Path $publishRoot | Out-Null
        New-Item -ItemType File -Path (Join-Path $publishRoot 'web.config') | Out-Null

        Mock Get-DeploymentCertificate { [pscustomobject]@{} }
        Mock Import-Module {}
        Mock Assert-IisDeploymentEnvironment {}
        Mock Get-Website {
            [pscustomobject]@{
                Name = 'Windows Script Runner'
                PhysicalPath = $publishRoot
                ApplicationPool = 'Target Pool'
                State = 'Started'
            }
        }
        Mock Get-WebAppPoolState { [pscustomobject]@{ Value = 'Started' } }
        Mock Get-WebBinding {
            [pscustomobject]@{
                bindingInformation = '*:443:windows-script-runner.local'
                sslFlags = 1
            }
        }
        Mock Get-Item {
            param([string[]]$LiteralPath)

            $path = $LiteralPath[0]
            if ($path -like 'IIS:\SslBindings\*') {
                return [pscustomobject]@{ Thumbprint = '9999999999999999999999999999999999999999' }
            }
            if ([IO.Directory]::Exists($path)) {
                return [IO.DirectoryInfo]$path
            }
            if ([IO.File]::Exists($path)) {
                return [IO.FileInfo]$path
            }
        }
        $parameters = @{
            PublishRoot = $publishRoot
            CertificateThumbprint = $testThumbprint
            SiteName = 'Windows Script Runner'
            AppPoolName = 'Target Pool'
        }

        { & $iisVerifierPath @parameters } | Should -Throw '*does not match the expected certificate*'
    }
}

Describe 'Artifact installation guard' {
    BeforeAll {
        $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:artifactInstallerPath = Join-Path $repositoryRoot 'deployment\powershell\Install-ReviewedAutomationArtifact.ps1'
        $script:sourceArtifactPath = Join-Path $repositoryRoot 'src\WindowsScriptRunner.Automation\Artifacts\windows.local-host-inventory\1.0.0\Collect-LocalHostInventory.ps1'
        $script:relativeArtifactPath = 'automation\windows.local-host-inventory\1.0.0\Collect-LocalHostInventory.ps1'
        $script:machineGuid = (Get-ItemPropertyValue -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid).ToString()
        function script:New-TestArtifactPublishRoot {
            param(
                [Parameter(Mandatory)]
                [string]$Path
            )

            $artifactDirectory = Split-Path -Parent (Join-Path $Path $script:relativeArtifactPath)
            New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
            Copy-Item -LiteralPath $script:sourceArtifactPath -Destination (Join-Path $Path $script:relativeArtifactPath)
        }
    }

    It 'returns WhatIf without installing an artifact' {
        $publishRoot = Join-Path $TestDrive 'artifact-publish'
        $installRoot = Join-Path $TestDrive 'artifact-install'
        New-TestArtifactPublishRoot $publishRoot
        New-Item -ItemType Directory -Path $installRoot | Out-Null
        $parameters = @{
            PublishRoot = $publishRoot
            InstallRoot = $installRoot
            ExpectedMachineGuid = $machineGuid
            ServiceAccount = 'NT SERVICE\WindowsScriptRunner.Worker'
            WhatIf = $true
        }

        $result = & $artifactInstallerPath @parameters

        $result.Status | Should -Be 'WhatIf'
        $result.Installed | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $installRoot $relativeArtifactPath) | Should -BeFalse
    }

    It 'rejects a reparse point at the final artifact destination' {
        $publishRoot = Join-Path $TestDrive 'artifact-publish-reparse'
        $installRoot = Join-Path $TestDrive 'artifact-install-reparse'
        $outsideRoot = Join-Path $TestDrive 'artifact-outside'
        New-TestArtifactPublishRoot $publishRoot
        New-Item -ItemType Directory -Path $installRoot | Out-Null
        New-Item -ItemType Directory -Path $outsideRoot | Out-Null
        $automationJunction = Join-Path $installRoot 'automation'
        New-Item -ItemType Junction -Path $automationJunction -Target $outsideRoot | Out-Null
        $parameters = @{
            PublishRoot = $publishRoot
            InstallRoot = $installRoot
            ExpectedMachineGuid = $machineGuid
            ServiceAccount = 'NT SERVICE\WindowsScriptRunner.Worker'
            WhatIf = $true
        }
        try {
            { & $artifactInstallerPath @parameters } | Should -Throw '*reparse point*'
        }
        finally {
            Remove-Item -LiteralPath $automationJunction -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Reviewed SQL migration' {
    BeforeAll {
        $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:migrationPath = Join-Path $repositoryRoot 'deployment\sql\Invoke-ReviewedMigration.ps1'
        $script:machineGuid = (Get-ItemPropertyValue -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid).ToString()
        function script:New-FakeSqlCmd {
            param(
                [Parameter(Mandatory)]
                [string]$Path,

                [Parameter(Mandatory)]
                [string]$LogPath
            )

            @(
                '@echo off'
                ('echo %* >> "{0}"' -f $LogPath)
                'echo %* | findstr /C:"RESTORE VERIFYONLY" >nul'
                'if not errorlevel 1 if "%WSR_TEST_FAIL_VERIFY%"=="1" exit /b 7'
                'exit /b 0'
            ) | Set-Content -LiteralPath $Path -Encoding Ascii
        }
    }

    BeforeEach {
        $script:sqlScriptPath = Join-Path $TestDrive 'migration.sql'
        $script:backupPath = Join-Path $TestDrive 'migration.bak'
        $script:sqlLogPath = Join-Path $TestDrive 'sqlcmd.log'
        $script:sqlCmdPath = Join-Path $TestDrive 'sqlcmd.cmd'
        New-Item -ItemType File -Path $script:sqlScriptPath -Force | Out-Null
        New-FakeSqlCmd $script:sqlCmdPath $script:sqlLogPath
    }

    It 'verifies the backup before applying the migration' {
        $oldFailureMode = $env:WSR_TEST_FAIL_VERIFY
        try {
            Remove-Item -LiteralPath $script:sqlLogPath -Force -ErrorAction SilentlyContinue
            $env:WSR_TEST_FAIL_VERIFY = $null
            $parameters = @{
                ServerInstance = 'localhost'
                Database = 'WindowsScriptRunner'
                SqlScriptPath = $script:sqlScriptPath
                BackupPath = $script:backupPath
                ExpectedMachineGuid = $machineGuid
                SqlCmdPath = $script:sqlCmdPath
                Confirm = $false
            }
            $result = & $migrationPath @parameters

            $result.Status | Should -Be 'Applied'
            $result.BackupCreated | Should -BeTrue
            $result.BackupVerified | Should -BeTrue
            $result.MigrationApplied | Should -BeTrue
            $commands = @(Get-Content -LiteralPath $script:sqlLogPath)
            $commands.Count | Should -Be 3
            $commands[0] | Should -Match 'BACKUP DATABASE'
            $commands[1] | Should -Match 'RESTORE VERIFYONLY'
            $commands[2] | Should -Match '\-i'
        }
        finally {
            $env:WSR_TEST_FAIL_VERIFY = $oldFailureMode
        }
    }

    It 'does not apply a migration when backup verification fails' {
        $oldFailureMode = $env:WSR_TEST_FAIL_VERIFY
        try {
            Remove-Item -LiteralPath $script:sqlLogPath -Force -ErrorAction SilentlyContinue
            $env:WSR_TEST_FAIL_VERIFY = '1'
            $parameters = @{
                ServerInstance = 'localhost'
                Database = 'WindowsScriptRunner'
                SqlScriptPath = $script:sqlScriptPath
                BackupPath = $script:backupPath
                ExpectedMachineGuid = $machineGuid
                SqlCmdPath = $script:sqlCmdPath
                Confirm = $false
            }

            { & $migrationPath @parameters } | Should -Throw '*exit code 7*'

            $commands = @(Get-Content -LiteralPath $script:sqlLogPath)
            $commands.Count | Should -Be 2
            ($commands -join [Environment]::NewLine) | Should -Not -Match '\-i'
        }
        finally {
            $env:WSR_TEST_FAIL_VERIFY = $oldFailureMode
        }
    }
}
