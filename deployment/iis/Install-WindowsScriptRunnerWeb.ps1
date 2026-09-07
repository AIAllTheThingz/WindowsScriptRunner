[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [string]$PublishRoot,

    [Parameter(Mandatory)]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory)]
    [string]$ExpectedMachineGuid,

    [string]$SiteName = 'WindowsScriptRunner',
    [string]$AppPoolName = 'WindowsScriptRunner',
    [string]$HostName = 'windows-script-runner.local',
    [int]$Port = 443
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\common\DeploymentAssertions.ps1')

Assert-WindowsDeploymentHost
Assert-NativeWindowsPowerShell51
Assert-DeploymentTargetMachine $ExpectedMachineGuid
Assert-IisTargetName $SiteName 'SiteName'
Assert-IisTargetName $AppPoolName 'AppPoolName'
Assert-IisHostName $HostName
if (-not $WhatIfPreference) {
    Assert-DeploymentAdministrator
}

$resolvedPublishRoot = Resolve-DeploymentAbsolutePath $PublishRoot 'PublishRoot'
Assert-DeploymentDirectory $resolvedPublishRoot 'PublishRoot'
Assert-DeploymentFile (Join-Path $resolvedPublishRoot 'web.config') 'ASP.NET Core web.config'

if ($Port -notin 1..65535) {
    throw 'Port must be between 1 and 65535.'
}
$null = Get-DeploymentCertificate $CertificateThumbprint
Import-Module WebAdministration -ErrorAction Stop
Assert-IisDeploymentEnvironment

$bindingInformation = '*:{0}:{1}' -f $Port, $HostName
$sslBindingPath = "IIS:\SslBindings\0.0.0.0!$Port!$HostName"
$appPoolPath = "IIS:\AppPools\$AppPoolName"

$sharedHttpsBinding = $null
foreach ($website in @(Get-Website)) {
    if (([string]$website.Name).Equals($SiteName, [StringComparison]::OrdinalIgnoreCase)) {
        continue
    }
    $sharedHttpsBinding = Get-WebBinding -Name $website.Name -Protocol https -Port $Port -HostHeader $HostName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $sharedHttpsBinding) {
        break
    }
}
if ($null -ne $sharedHttpsBinding) {
    throw 'Another IIS site already has an HTTPS binding for the requested host and port.'
}

$appPoolExists = Test-Path -LiteralPath $appPoolPath

if ($appPoolExists) {
    $sharedSite = Get-Website | Where-Object {
        (-not ([string]$_.Name).Equals($SiteName, [StringComparison]::OrdinalIgnoreCase)) -and
        ([string]$_.ApplicationPool).Equals($AppPoolName, [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    $sharedApplication = $null
    if ($null -eq $sharedSite) {
        foreach ($website in @(Get-Website)) {
            $sharedApplication = Get-WebApplication -Site $website.Name -ErrorAction SilentlyContinue |
                Where-Object {
                    ([string]$_.ApplicationPool).Equals($AppPoolName, [StringComparison]::OrdinalIgnoreCase) -and
                    (
                        (-not ([string]$website.Name).Equals($SiteName, [StringComparison]::OrdinalIgnoreCase)) -or
                        ([string]$_.Path) -ne '/'
                    )
                } |
                Select-Object -First 1
            if ($null -ne $sharedApplication) {
                break
            }
        }
    }
    if ($null -ne $sharedSite -or $null -ne $sharedApplication) {
        throw "IIS application pool '$AppPoolName' is already used by another site or application."
    }
}

$configured = $false
$verified = $false
if ($PSCmdlet.ShouldProcess($SiteName, "Configure IIS HTTPS site on port $Port")) {
    if (-not $appPoolExists) {
        New-WebAppPool -Name $AppPoolName | Out-Null
    }

    Set-ItemProperty -LiteralPath $appPoolPath -Name managedRuntimeVersion -Value ''
    Set-ItemProperty -LiteralPath $appPoolPath -Name managedPipelineMode -Value 'Integrated'
    Set-ItemProperty -LiteralPath $appPoolPath -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty -LiteralPath $appPoolPath -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    Set-ItemProperty -LiteralPath $appPoolPath -Name processModel.loadUserProfile -Value $false
    Set-ItemProperty -LiteralPath $appPoolPath -Name failure.rapidFailProtection -Value $true

    $site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
    if ($null -eq $site) {
        New-Website `
            -Name $SiteName `
            -PhysicalPath $resolvedPublishRoot `
            -ApplicationPool $AppPoolName `
            -IPAddress '*' `
            -Port $Port `
            -HostHeader $HostName `
            -Ssl `
            -SslFlags 1 |
            Out-Null
    }
    else {
        Set-ItemProperty -LiteralPath "IIS:\Sites\$SiteName" -Name physicalPath -Value $resolvedPublishRoot
        Set-ItemProperty -LiteralPath "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    }

    $matchingBindings = @(
        Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue |
            Where-Object {
                ([string]$_.bindingInformation).Equals(
                    $bindingInformation,
                    [StringComparison]::OrdinalIgnoreCase)
            }
    )
    if ($matchingBindings.Count -eq 0) {
        New-WebBinding `
            -Name $SiteName `
            -Protocol https `
            -IPAddress '*' `
            -Port $Port `
            -HostHeader $HostName `
            -SslFlags 1 |
            Out-Null
    }
    elseif ($matchingBindings.Count -ne 1) {
        throw "IIS site '$SiteName' has more than one HTTPS binding for the requested IP, port, and host."
    }
    else {
        Set-WebBinding `
            -Name $SiteName `
            -BindingInformation $bindingInformation `
            -PropertyName sslFlags `
            -Value '1' |
            Out-Null
    }

    if (Test-Path -LiteralPath $sslBindingPath) {
        Remove-Item -LiteralPath $sslBindingPath -Force
    }
    New-Item -Path $sslBindingPath -Thumbprint $CertificateThumbprint -SSLFlags 1 | Out-Null

    Set-WebConfigurationProperty `
        -PSPath "IIS:\Sites\$SiteName" `
        -Filter '/system.webServer/security/authentication/windowsAuthentication' `
        -Name enabled `
        -Value $true

    $webAcl = 'IIS AppPool\{0}:(OI)(CI)(RX)' -f $AppPoolName
    Invoke-DeploymentNativeCommand 'icacls.exe' @($resolvedPublishRoot, '/grant', $webAcl, '/T', '/C')
    Start-WebAppPool -Name $AppPoolName
    Start-Website -Name $SiteName

    & (Join-Path $PSScriptRoot 'Verify-WindowsScriptRunnerWeb.ps1') `
        -PublishRoot $resolvedPublishRoot `
        -CertificateThumbprint $CertificateThumbprint `
        -SiteName $SiteName `
        -AppPoolName $AppPoolName `
        -HostName $HostName `
        -Port $Port |
        Out-Null
    $configured = $true
    $verified = $true
}

$status = if ($configured) {
    'Applied'
}
elseif ($WhatIfPreference) {
    'WhatIf'
}
else {
    'Declined'
}

[pscustomobject]@{
    SiteName = $SiteName
    AppPoolName = $AppPoolName
    PublishRoot = $resolvedPublishRoot
    HostName = $HostName
    Port = $Port
    CertificateThumbprint = $CertificateThumbprint.ToUpperInvariant()
    Status = $status
    Configured = $configured
    Verified = $verified
}
