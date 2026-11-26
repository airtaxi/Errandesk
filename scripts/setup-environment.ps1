#Requires -Version 5.1
<#
.SYNOPSIS
Checks or installs the tools that build the Errandesk Android and Windows clients.

.DESCRIPTION
Inspects the machine for the .NET 11 SDK, the Android workload and SDK, a JDK,
the Windows SDK, and aticmsixgen. With -Install, it installs missing tools with
winget and the dotnet CLI. It never creates signing keys or downloads
google-services.json.

.PARAMETER Install
Installs missing tools. Without this switch the script only reports status.

.PARAMETER Check
Reports status without installing anything. This is the default behavior.

.PARAMETER IncludeIos
Also checks or installs the ios workload. The Android and Windows build scripts
skip the iOS target, so this workload is only needed for solution builds.

.PARAMETER SkipAndroid
Skips the Android workload, Android SDK, JDK, and adb checks.

.PARAMETER SkipWindows
Skips the Windows SDK and aticmsixgen checks.

.PARAMETER AndroidSdkDirectory
Android SDK folder to install or check. Defaults to ANDROID_HOME, ANDROID_SDK_ROOT,
then %LOCALAPPDATA%\Android\Sdk.

.PARAMETER JavaSdkDirectory
Java SDK folder passed to InstallAndroidDependencies when a JDK must be installed.

.PARAMETER RepositoryRoot
Errandesk repository root. Defaults to the app repository that contains this scripts
folder, or to the Errandesk folder next to a separate build repository.

.PARAMETER Force
Updates aticmsixgen even when it is already installed.

.EXAMPLE
.\setup-environment.ps1 -Check

.EXAMPLE
.\setup-environment.ps1 -Install

.EXAMPLE
.\setup-environment.ps1 -Install -IncludeIos
#>
[CmdletBinding()]
param(
    [switch] $Install,
    [switch] $Check,
    [switch] $IncludeIos,
    [switch] $SkipAndroid,
    [switch] $SkipWindows,
    [string] $AndroidSdkDirectory,
    [string] $JavaSdkDirectory,
    [string] $RepositoryRoot,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

trap {
    Write-Host ''
    Write-Host "The environment check failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage -ForegroundColor Red
    if (Test-LaunchedFromFileManager) {
        $null = Read-Host 'Press Enter to close this window'
    }
    exit 1
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-NativeCommand {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [string] $Description
    )

    Write-Host $Description
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $FilePath @Arguments 2>&1 | Out-Host
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorAction
    if ($exitCode -ne 0) { throw "$Description failed with exit code $exitCode." }
}

function Install-WingetPackage {
    param([string] $Id)

    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Warning "winget was not found. Install $Id manually."
        return $false
    }

    Invoke-NativeCommand -FilePath $winget.Source -Arguments @('install', '--id', $Id, '--exact', '--accept-package-agreements', '--accept-source-agreements') -Description "Installing $Id"
    return $true
}

function Find-WindowsKitTool {
    param([string] $Name)

    $windowsKitRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $windowsKitRoot -PathType Container)) { return $null }

    $preferred = Join-Path $windowsKitRoot "10.0.26100.0\x64\$Name"
    if (Test-Path -LiteralPath $preferred -PathType Leaf) { return $preferred }

    $candidate = Get-ChildItem -Path $windowsKitRoot -Recurse -Filter $Name -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($candidate) { return $candidate.FullName }
    return $null
}

function Find-Dotnet {
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command }

    # A freshly installed SDK is not on the PATH of the current session yet.
    $installedPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $installedPath -PathType Leaf) { return Get-Command -Name $installedPath }
    return $null
}

function Test-AticMsixGenInstalled {
    if (Get-Command aticmsixgen -ErrorAction SilentlyContinue) { return $true }

    # A freshly installed global tool is not on the PATH of the current session yet.
    foreach ($name in @('aticmsixgen.exe', 'aticmsixgen.cmd')) {
        if (Test-Path -LiteralPath (Join-Path $env:USERPROFILE ".dotnet\tools\$name") -PathType Leaf) { return $true }
    }
    return $false
}

function Find-Keytool {
    $keytool = Get-Command keytool.exe -ErrorAction SilentlyContinue
    if ($keytool) { return $keytool.Source }

    if (-not [string]::IsNullOrWhiteSpace($JavaSdkDirectory)) {
        $candidate = Get-ChildItem -Path $JavaSdkDirectory -Recurse -Filter 'keytool.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    $javaHome = [Environment]::GetEnvironmentVariable('JAVA_HOME')
    if (-not [string]::IsNullOrWhiteSpace($javaHome)) {
        $candidate = Join-Path $javaHome 'bin\keytool.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }

    foreach ($root in @((Join-Path $env:ProgramFiles 'Microsoft'), (Join-Path $env:LOCALAPPDATA 'Microsoft'))) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        $candidate = Get-ChildItem -Path $root -Directory -Filter 'jdk-*' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin\keytool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    return $null
}

function Find-AndroidSdk {
    foreach ($candidate in @($AndroidSdkDirectory, [Environment]::GetEnvironmentVariable('ANDROID_HOME'), [Environment]::GetEnvironmentVariable('ANDROID_SDK_ROOT'), (Join-Path $env:LOCALAPPDATA 'Android\Sdk'), (Join-Path ${env:ProgramFiles(x86)} 'Android\android-sdk'))) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        if (Test-Path -LiteralPath $candidate -PathType Container) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    return $null
}

function Test-AndroidSdk {
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return (Test-Path -LiteralPath (Join-Path $Path 'platforms') -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $Path 'build-tools') -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $Path 'platform-tools') -PathType Container)
}

$script:FailureCount = 0
function Write-Status {
    param(
        [string] $Name,
        [bool] $Ok,
        [string] $Detail,
        [switch] $Optional
    )

    if ($Ok) {
        Write-Host ("[OK]   {0}: {1}" -f $Name, $Detail) -ForegroundColor Green
    }
    elseif ($Optional) {
        Write-Host ("[SKIP] {0}: {1}" -f $Name, $Detail) -ForegroundColor Yellow
    }
    else {
        Write-Host ("[MISS] {0}: {1}" -f $Name, $Detail) -ForegroundColor Red
        $script:FailureCount++
    }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = $defaultRepositoryRoot }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

if ($Install -and $Check) { throw 'Use either -Check or -Install, not both.' }

Write-Host 'Errandesk build environment'
Write-Host ''

$osVersion = [Environment]::OSVersion.Version
Write-Status 'Windows' ($osVersion.Major -ge 10 -and $osVersion.Build -ge 19041) "build $($osVersion.Build)"
Write-Status 'PowerShell' ($PSVersionTable.PSVersion -ge [Version]'5.1') $PSVersionTable.PSVersion.ToString()

if ($Install -and -not (Test-Administrator)) {
    Write-Warning 'Run the terminal as Administrator if a workload or SDK installation fails.'
}

$dotnet = Find-Dotnet
if (-not $dotnet -and $Install) {
    Install-WingetPackage 'Microsoft.DotNet.SDK.11' | Out-Null
    $dotnet = Find-Dotnet
}

if (-not $dotnet) {
    Write-Status '.NET 11 SDK' $false 'dotnet was not found on PATH.'
}
else {
    $sdkOutput = @(& $dotnet.Source --list-sdks 2>$null)
    $sdkVersion = @($sdkOutput | Where-Object { $_ -match '^11\.0\.\d+' } | Select-Object -First 1)
    if ($sdkVersion.Count -eq 0 -and $Install) {
        Install-WingetPackage 'Microsoft.DotNet.SDK.11' | Out-Null
        $sdkOutput = @(& $dotnet.Source --list-sdks 2>$null)
        $sdkVersion = @($sdkOutput | Where-Object { $_ -match '^11\.0\.\d+' } | Select-Object -First 1)
    }
    $sdkDetail = if ($sdkVersion.Count -gt 0) { $sdkVersion[0] } else { 'not installed' }
    Write-Status '.NET 11 SDK' ($sdkVersion.Count -gt 0) $sdkDetail
}

$workloadOutput = @()
if ($dotnet) { $workloadOutput = @(& $dotnet.Source workload list 2>$null) }

if (-not $SkipAndroid) {
    $hasAndroid = @($workloadOutput | Where-Object { $_ -match '^android\s' }).Count -gt 0
    if (-not $hasAndroid -and $Install -and $dotnet) {
        Invoke-NativeCommand -FilePath $dotnet.Source -Arguments @('workload', 'install', 'android') -Description 'Installing the android workload'
        $workloadOutput = @(& $dotnet.Source workload list 2>$null)
        $hasAndroid = @($workloadOutput | Where-Object { $_ -match '^android\s' }).Count -gt 0
    }
    $androidWorkloadDetail = if ($hasAndroid) { 'installed' } else { 'run dotnet workload install android' }
    Write-Status '.NET android workload' $hasAndroid $androidWorkloadDetail
}

if ($IncludeIos) {
    $hasIos = @($workloadOutput | Where-Object { $_ -match '^ios\s' }).Count -gt 0
    if (-not $hasIos -and $Install -and $dotnet) {
        Invoke-NativeCommand -FilePath $dotnet.Source -Arguments @('workload', 'install', 'ios') -Description 'Installing the ios workload'
        $workloadOutput = @(& $dotnet.Source workload list 2>$null)
        $hasIos = @($workloadOutput | Where-Object { $_ -match '^ios\s' }).Count -gt 0
    }
    $iosWorkloadDetail = if ($hasIos) { 'installed' } else { 'run dotnet workload install ios' }
    Write-Status '.NET ios workload' $hasIos $iosWorkloadDetail -Optional
}

if (-not $SkipAndroid) {
    $androidSdk = Find-AndroidSdk
    $androidSdkOk = Test-AndroidSdk $androidSdk

    if ((-not $androidSdk -or -not $androidSdkOk) -and $Install) {
        if (-not $dotnet) { throw 'The .NET SDK is required to install the Android SDK.' }
        if ([string]::IsNullOrWhiteSpace($AndroidSdkDirectory)) {
            $AndroidSdkDirectory = if ($androidSdk) { $androidSdk } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
        }
        $arguments = @(
            'build', (Join-Path $RepositoryRoot 'src\Errandesk.Client\Errandesk.Client.csproj'),
            '-f', 'net11.0-android',
            '-t:InstallAndroidDependencies',
            "-p:AndroidSdkDirectory=$AndroidSdkDirectory",
            '-p:AcceptAndroidSDKLicenses=True',
            '-p:ErrandeskIncludeIosTarget=false',
            '-nologo',
            '-v', 'minimal'
        )
        if (-not [string]::IsNullOrWhiteSpace($JavaSdkDirectory)) { $arguments += "-p:JavaSdkDirectory=$JavaSdkDirectory" }
        Invoke-NativeCommand -FilePath $dotnet.Source -Arguments $arguments -Description 'Installing the Android SDK'
        $androidSdk = Find-AndroidSdk
        $androidSdkOk = Test-AndroidSdk $androidSdk
    }

    $androidSdkDetail = if ($androidSdk) { $androidSdk } else { 'not found; run InstallAndroidDependencies or install the Android SDK' }
    Write-Status 'Android SDK' $androidSdkOk $androidSdkDetail

    if ($androidSdk -and $Install -and [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('ANDROID_HOME'))) {
        [Environment]::SetEnvironmentVariable('ANDROID_HOME', $androidSdk, 'User')
        $env:ANDROID_HOME = $androidSdk
        Write-Host "Set the user environment variable ANDROID_HOME to $androidSdk."
    }

    $keytool = Find-Keytool
    if (-not $keytool -and $Install) {
        if (Install-WingetPackage 'Microsoft.OpenJDK.17') { $keytool = Find-Keytool }
    }
    $jdkOk = -not [string]::IsNullOrWhiteSpace($keytool)
    $jdkDetail = if ($jdkOk) { $keytool } else { 'install JDK 17 or later' }
    Write-Status 'JDK (keytool)' $jdkOk $jdkDetail

    if ($jdkOk -and $Install -and [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('JAVA_HOME'))) {
        $javaHome = Split-Path -Parent (Split-Path -Parent $keytool)
        [Environment]::SetEnvironmentVariable('JAVA_HOME', $javaHome, 'User')
        $env:JAVA_HOME = $javaHome
        Write-Host "Set the user environment variable JAVA_HOME to $javaHome."
    }

    $adb = $null
    if ($androidSdk) {
        $adbCandidate = Join-Path $androidSdk 'platform-tools\adb.exe'
        if (Test-Path -LiteralPath $adbCandidate -PathType Leaf) { $adb = $adbCandidate }
    }
    if (-not $adb) {
        $adbCommand = Get-Command adb.exe -ErrorAction SilentlyContinue
        if ($adbCommand) { $adb = $adbCommand.Source }
    }
    $adbDetail = if ($adb) { $adb } else { 'install Android platform-tools for -Install' }
    Write-Status 'adb' (-not [string]::IsNullOrWhiteSpace($adb)) $adbDetail -Optional
}

if (-not $SkipWindows) {
    $makeAppx = Find-WindowsKitTool 'makeappx.exe'
    $signTool = Find-WindowsKitTool 'signtool.exe'
    $windowsSdkOk = -not [string]::IsNullOrWhiteSpace($makeAppx) -and -not [string]::IsNullOrWhiteSpace($signTool)

    if (-not $windowsSdkOk -and $Install) {
        if (Install-WingetPackage 'Microsoft.WindowsSDK.10.0.26100') {
            $makeAppx = Find-WindowsKitTool 'makeappx.exe'
            $signTool = Find-WindowsKitTool 'signtool.exe'
            $windowsSdkOk = -not [string]::IsNullOrWhiteSpace($makeAppx) -and -not [string]::IsNullOrWhiteSpace($signTool)
        }
    }
    $windowsSdkDetail = if ($windowsSdkOk) { Split-Path -Parent $makeAppx } else { 'makeappx.exe and signtool.exe were not found' }
    Write-Status 'Windows SDK' $windowsSdkOk $windowsSdkDetail

    $aticMsixGenOk = Test-AticMsixGenInstalled
    if (-not $aticMsixGenOk -and $Install -and $dotnet) {
        Invoke-NativeCommand -FilePath $dotnet.Source -Arguments @('tool', 'install', '--global', 'aticmsixgen') -Description 'Installing aticmsixgen'
        $aticMsixGenOk = Test-AticMsixGenInstalled
    }
    elseif ($aticMsixGenOk -and $Force -and $dotnet) {
        Invoke-NativeCommand -FilePath $dotnet.Source -Arguments @('tool', 'update', '--global', 'aticmsixgen') -Description 'Updating aticmsixgen'
    }
    $aticMsixGenDetail = if ($aticMsixGenOk) { 'installed' } else { 'run dotnet tool install --global aticmsixgen' }
    Write-Status 'aticmsixgen' $aticMsixGenOk $aticMsixGenDetail
}

Write-Host ''
if ($script:FailureCount -eq 0) {
    Write-Host 'The required build tools are available.' -ForegroundColor Green
}
else {
    Write-Host "$($script:FailureCount) required item(s) are missing." -ForegroundColor Red
    if (-not $Install) { Write-Host 'Run this script with -Install to install the missing tools.' }
}

if (Test-LaunchedFromFileManager) {
    $null = Read-Host 'Press Enter to close this window'
}

if ($script:FailureCount -eq 0) { exit 0 }
exit 1
