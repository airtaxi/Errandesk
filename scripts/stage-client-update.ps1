#Requires -Version 5.1
<#
.SYNOPSIS
Stages the files that the Errandesk Windows and Android clients download for updates.

.DESCRIPTION
Creates the windows and android folders expected by ERRANDESK_CLIENT_UPDATE_BASE_URL.
Packages are copied first and the version files are written last, so a client never
sees a new version before its package is available.

.PARAMETER Destination
Folder that receives the update layout. Defaults to client-updates in the local root.

.PARAMETER AndroidVersion
Android version written to android\version. Defaults to the Version line in build-info.txt.

.PARAMETER WindowsVersion
Windows version written to windows\version. Defaults to the MSIX version line in
windows-build-info.txt.

.PARAMETER AndroidApk
Android APK to stage. Defaults to Errandesk-latest.apk in the local root.

.PARAMETER WindowsX64Installer
x64 installer to stage. Defaults to Installer-x64.exe in the local root.

.PARAMETER WindowsArm64Installer
ARM64 installer to stage. Defaults to Installer-arm64.exe in the local root.

.PARAMETER SkipAndroid
Skip the Android files.

.PARAMETER SkipWindows
Skip the Windows files.

.EXAMPLE
.\stage-client-update.ps1

.EXAMPLE
.\stage-client-update.ps1 -Destination D:\www\errandesk\updates
#>
[CmdletBinding()]
param(
    [string] $Destination,
    [string] $AndroidVersion,
    [string] $WindowsVersion,
    [string] $AndroidApk,
    [string] $WindowsX64Installer,
    [string] $WindowsArm64Installer,
    [switch] $SkipAndroid,
    [switch] $SkipWindows
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

trap {
    Write-Host ''
    Write-Host "Staging the client update failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage -ForegroundColor Red
    if (Test-LaunchedFromFileManager) {
        $null = Read-Host 'Press Enter to close this window'
    }
    exit 1
}

function Get-InfoValue {
    param(
        [string] $Path,
        [string] $Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $match = [regex]::Match((Get-Content -LiteralPath $Path -Raw), "(?m)^$([regex]::Escape($Name)):\s*(.+?)\s*$")
    if ($match.Success) { return $match.Groups[1].Value }
    return $null
}

function Write-TextFile {
    param(
        [string] $Path,
        [string] $Value
    )

    [System.IO.File]::WriteAllText($Path, "$Value`n", (New-Object System.Text.UTF8Encoding($false)))
}

if ($SkipAndroid -and $SkipWindows) { throw 'Both platforms are skipped.' }
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $script:LocalRoot 'client-updates' }
$Destination = Resolve-InputPath $Destination

if (-not $SkipAndroid) {
    if ([string]::IsNullOrWhiteSpace($AndroidApk)) { $AndroidApk = Join-Path $script:LocalRoot 'Errandesk-latest.apk' }
    $AndroidApk = Resolve-InputPath $AndroidApk
    if (-not (Test-Path -LiteralPath $AndroidApk -PathType Leaf)) { throw "The Android APK does not exist: $AndroidApk" }
    if ([string]::IsNullOrWhiteSpace($AndroidVersion)) { $AndroidVersion = Get-InfoValue -Path (Join-Path $script:LocalRoot 'build-info.txt') -Name 'Version' }
    if ([string]::IsNullOrWhiteSpace($AndroidVersion)) { throw 'The Android version was not found. Pass -AndroidVersion.' }
    if ($AndroidVersion -notmatch '^\d+(\.\d+){0,3}$') { throw "The Android version is not numeric: $AndroidVersion" }
}

if (-not $SkipWindows) {
    if ([string]::IsNullOrWhiteSpace($WindowsX64Installer)) { $WindowsX64Installer = Join-Path $script:LocalRoot 'Installer-x64.exe' }
    if ([string]::IsNullOrWhiteSpace($WindowsArm64Installer)) { $WindowsArm64Installer = Join-Path $script:LocalRoot 'Installer-arm64.exe' }
    $WindowsX64Installer = Resolve-InputPath $WindowsX64Installer
    $WindowsArm64Installer = Resolve-InputPath $WindowsArm64Installer
    if (-not (Test-Path -LiteralPath $WindowsX64Installer -PathType Leaf)) { throw "The x64 installer does not exist: $WindowsX64Installer. Build without -SkipInstallers." }
    if (-not (Test-Path -LiteralPath $WindowsArm64Installer -PathType Leaf)) { throw "The ARM64 installer does not exist: $WindowsArm64Installer. Build without -SkipInstallers." }
    if ([string]::IsNullOrWhiteSpace($WindowsVersion)) { $WindowsVersion = Get-InfoValue -Path (Join-Path $script:LocalRoot 'windows-build-info.txt') -Name 'MSIX version' }
    if ([string]::IsNullOrWhiteSpace($WindowsVersion)) { throw 'The Windows version was not found. Pass -WindowsVersion.' }
    if ($WindowsVersion -notmatch '^\d+(\.\d+){0,3}$') { throw "The Windows version is not numeric: $WindowsVersion" }
}

if (-not $SkipAndroid) {
    $androidDirectory = Join-Path $Destination 'android'
    New-Item -ItemType Directory -Force -Path $androidDirectory | Out-Null
    Copy-Item -LiteralPath $AndroidApk -Destination (Join-Path $androidDirectory 'Errandesk-latest.apk') -Force
}

if (-not $SkipWindows) {
    $windowsDirectory = Join-Path $Destination 'windows'
    New-Item -ItemType Directory -Force -Path $windowsDirectory | Out-Null
    Copy-Item -LiteralPath $WindowsX64Installer -Destination (Join-Path $windowsDirectory 'Installer-x64.exe') -Force
    Copy-Item -LiteralPath $WindowsArm64Installer -Destination (Join-Path $windowsDirectory 'Installer-arm64.exe') -Force
}

if (-not $SkipAndroid) { Write-TextFile -Path (Join-Path $Destination 'android\version') -Value $AndroidVersion }
if (-not $SkipWindows) { Write-TextFile -Path (Join-Path $Destination 'windows\version') -Value $WindowsVersion }

Write-Host ''
Write-Host "Client update files were staged in $Destination"
if (-not $SkipAndroid) { Write-Host "Android: $AndroidVersion ($Destination\android\Errandesk-latest.apk)" }
if (-not $SkipWindows) { Write-Host "Windows: $WindowsVersion ($Destination\windows\Installer-x64.exe, $Destination\windows\Installer-arm64.exe)" }
Write-Host 'Upload the packages first and replace each version file last.'

if (Test-LaunchedFromFileManager) {
    $null = Read-Host 'Press Enter to close this window'
}
