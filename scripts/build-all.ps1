#Requires -Version 5.1
<#
.SYNOPSIS
Builds the Android and Windows release packages in sequence.

.DESCRIPTION
Runs build-android.ps1 and build-windows-msix.ps1 as child processes. A failure in
either build stops the sequence and returns a non-zero exit code.

.PARAMETER RepositoryRoot
Errandesk repository root. Defaults to the app repository that contains this scripts
folder, or to the Errandesk folder next to a separate build repository.

.PARAMETER Configuration
Build configuration. Defaults to Release.

.PARAMETER Version
Application display version passed to the Android build. The Windows build reads the
version from Directory.Build.props.

.PARAMETER VersionCode
Android version code. Defaults to the value calculated from the display version.

.PARAMETER Revision
Fourth part of the MSIX package version.

.PARAMETER AndroidSettingsFile
Android settings file. Defaults to errandesk-release.env in the local root.

.PARAMETER WindowsSettingsFile
Windows settings file. Defaults to windows-signing.env in the local root.

.PARAMETER OutputDirectory
Folder that receives the packages and build information for both builds. When unset, the
Android build uses ERRANDESK_ANDROID_OUTPUT_DIRECTORY from the environment or its settings
file, and each build falls back to the local root.

.PARAMETER Bundle
Also produce an Android App Bundle.

.PARAMETER Install
Install the Android APK on the connected device.

.PARAMETER SkipFirebase
Build the Android client without google-services.json and Firebase Cloud Messaging.

.PARAMETER SkipInstallers
Skip the Windows EXE installers.

.PARAMETER SkipAndroid
Skip the Android build.

.PARAMETER SkipWindows
Skip the Windows build.

.EXAMPLE
.\build-all.ps1

.EXAMPLE
.\build-all.ps1 -SkipWindows

.EXAMPLE
.\build-all.ps1 -SkipAndroid -SkipInstallers
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [string] $Configuration = 'Release',
    [string] $Version,
    [int] $VersionCode = 0,
    [int] $Revision = -1,
    [string] $AndroidSettingsFile,
    [string] $WindowsSettingsFile,
    [string] $OutputDirectory,
    [switch] $Bundle,
    [switch] $Install,
    [switch] $SkipFirebase,
    [switch] $SkipInstallers,
    [switch] $SkipAndroid,
    [switch] $SkipWindows
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

trap {
    Write-Host ''
    Write-Host "The build failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage -ForegroundColor Red
    if (Test-LaunchedFromFileManager) {
        $null = Read-Host 'Press Enter to close this window'
    }
    exit 1
}

function Invoke-BuildScript {
    param(
        [string] $ScriptPath,
        [string[]] $Arguments,
        [string] $Description
    )

    Write-Host ''
    Write-Host $Description -ForegroundColor Cyan
    & $powershellPath -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = $defaultRepositoryRoot }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

# Without -OutputDirectory, each build script chooses its own output folder.
$outputDirectoryArguments = @()
if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Resolve-InputPath $OutputDirectory
    $outputDirectoryArguments = @('-OutputDirectory', $OutputDirectory)
}

if ($SkipAndroid -and $SkipWindows) { throw 'Both platforms are skipped.' }

$powershellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $powershellPath -PathType Leaf)) {
    $powershellCommand = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if (-not $powershellCommand) { throw 'Windows PowerShell was not found.' }
    $powershellPath = $powershellCommand.Source
}

if (-not $SkipAndroid) {
    $arguments = @('-Configuration', $Configuration, '-RepositoryRoot', $RepositoryRoot) + $outputDirectoryArguments
    if (-not [string]::IsNullOrWhiteSpace($Version)) { $arguments += @('-Version', $Version) }
    if ($VersionCode -gt 0) { $arguments += @('-VersionCode', $VersionCode) }
    if (-not [string]::IsNullOrWhiteSpace($AndroidSettingsFile)) { $arguments += @('-SettingsFile', (Resolve-InputPath $AndroidSettingsFile)) }
    if ($Bundle) { $arguments += '-Bundle' }
    if ($Install) { $arguments += '-Install' }
    if ($SkipFirebase) { $arguments += '-SkipFirebase' }
    Invoke-BuildScript -ScriptPath (Join-Path $scriptDirectory 'build-android.ps1') -Arguments $arguments -Description 'Building the Android client'
}

if (-not $SkipWindows) {
    $arguments = @('-Configuration', $Configuration, '-RepositoryRoot', $RepositoryRoot) + $outputDirectoryArguments
    if ($Revision -ge 0) { $arguments += @('-Revision', $Revision) }
    if (-not [string]::IsNullOrWhiteSpace($WindowsSettingsFile)) { $arguments += @('-SettingsFile', (Resolve-InputPath $WindowsSettingsFile)) }
    if ($SkipInstallers) { $arguments += '-SkipInstallers' }
    Invoke-BuildScript -ScriptPath (Join-Path $scriptDirectory 'build-windows-msix.ps1') -Arguments $arguments -Description 'Building the Windows client'
}

if ($outputDirectoryArguments.Count -gt 0) {
    Write-Host ''
    Write-Host "Build outputs: $OutputDirectory"
    if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'build-info.txt')) { Write-Host "Android info: $(Join-Path $OutputDirectory 'build-info.txt')" }
    if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'windows-build-info.txt')) { Write-Host "Windows info: $(Join-Path $OutputDirectory 'windows-build-info.txt')" }
}

if (Test-LaunchedFromFileManager) {
    $null = Read-Host 'Press Enter to close this window'
}
