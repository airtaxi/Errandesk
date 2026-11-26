#Requires -Version 5.1
<#
.SYNOPSIS
Creates Android signing material for the Errandesk build scripts.

.DESCRIPTION
Creates an Android keystore, two password files, and an environment file when they do
not exist. The keystore and key alias passwords can differ; when they do, a JKS keystore
is created. The password files are stored under secrets and are not written to the
environment file. Existing signing material is left unchanged unless -Force is passed.

The Windows certificate is created by build-windows-msix.ps1 on its first run.

.PARAMETER Alias
Keystore key alias. Defaults to errandesk.

.PARAMETER Keystore
Keystore file. Defaults to ks in the local root.

.PARAMETER GoogleServicesJson
Firebase google-services.json. Defaults to google-services.json in the local root,
then the client's Platforms\Android folder.

.PARAMETER RepositoryRoot
Errandesk repository root. Defaults to the app repository that contains this scripts
folder, or to the Errandesk folder next to a separate build repository.

.PARAMETER PasswordFile
Reads one password from this file and uses it for both the keystore and the key.

.PARAMETER StorePasswordFile
Reads the keystore password from this file instead of prompting.

.PARAMETER KeyPasswordFile
Reads the key alias password from this file instead of prompting.

.PARAMETER Force
Replaces an existing keystore after moving the old files to a backup folder.

.EXAMPLE
.\setup-signing.ps1

.EXAMPLE
.\setup-signing.ps1 -Alias errandesk-release -Force
#>
[CmdletBinding()]
param(
    [string] $Alias = 'errandesk',
    [string] $Keystore,
    [string] $GoogleServicesJson,
    [string] $RepositoryRoot,
    [string] $PasswordFile,
    [string] $StorePasswordFile,
    [string] $KeyPasswordFile,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

trap {
    Write-Host ''
    Write-Host "The signing setup failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage -ForegroundColor Red
    if (Test-LaunchedFromFileManager) {
        $null = Read-Host 'Press Enter to close this window'
    }
    exit 1
}

function Exit-Script {
    param([int] $Code)

    if (Test-LaunchedFromFileManager) {
        $null = Read-Host 'Press Enter to close this window'
    }
    exit $Code
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

function Find-Keytool {
    $keytool = Get-Command keytool.exe -ErrorAction SilentlyContinue
    if ($keytool) { return $keytool.Source }

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

function Read-ConfirmedPassword {
    param([string] $Prompt)

    $password = Read-PlainTextPassword -Prompt $Prompt
    $confirmation = Read-PlainTextPassword -Prompt 'Confirm the password'
    if ($password -cne $confirmation) { throw 'The passwords do not match.' }
    return $password
}

function Read-PasswordFile {
    param(
        [string] $Path,
        [string] $Description
    )

    $resolvedPath = Resolve-InputPath $Path
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) { throw "The $Description file does not exist: $resolvedPath" }
    return (Get-Content -LiteralPath $resolvedPath -Raw).Trim()
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = $defaultRepositoryRoot }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

$keystorePath = if ([string]::IsNullOrWhiteSpace($Keystore)) { Join-Path $script:LocalRoot 'ks' } else { Resolve-InputPath $Keystore }
$settingsPath = Join-Path $script:LocalRoot 'errandesk-release.env'
$secretsDirectory = Join-Path $script:LocalRoot 'secrets'
$storePasswordPath = Join-Path $secretsDirectory 'android-keystore-password.txt'
$keyPasswordPath = Join-Path $secretsDirectory 'android-key-password.txt'

if ((Test-Path -LiteralPath $keystorePath -PathType Leaf) -and -not $Force) {
    Write-Host "The Android keystore already exists: $keystorePath"
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        Write-Host "The Android settings file already exists: $settingsPath"
    }
    else {
        Write-Warning "The settings file is missing. Create it from errandesk-release.env.example or pass -Force to replace the keystore."
    }
    Exit-Script 0
}

if ((Test-Path -LiteralPath $keystorePath -PathType Leaf) -and $Force) {
    $backupDirectory = Join-Path $secretsDirectory ("backup-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    Move-Item -LiteralPath $keystorePath -Destination (Join-Path $backupDirectory 'ks') -Force
    if (Test-Path -LiteralPath $storePasswordPath -PathType Leaf) { Move-Item -LiteralPath $storePasswordPath -Destination (Join-Path $backupDirectory 'android-keystore-password.txt') -Force }
    if (Test-Path -LiteralPath $keyPasswordPath -PathType Leaf) { Move-Item -LiteralPath $keyPasswordPath -Destination (Join-Path $backupDirectory 'android-key-password.txt') -Force }
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) { Move-Item -LiteralPath $settingsPath -Destination (Join-Path $backupDirectory 'errandesk-release.env') -Force }
    Write-Warning "The previous signing files were moved to $backupDirectory."
    Write-Warning 'A new keystore has a different signing identity. Existing installed apps cannot be updated with it.'
}

$keytool = Find-Keytool
if (-not $keytool) { throw 'keytool was not found. Run setup-environment.ps1 -Install, or install JDK 17 or later.' }

if (-not [string]::IsNullOrWhiteSpace($PasswordFile)) {
    $storePassword = Read-PasswordFile -Path $PasswordFile -Description 'password'
    $keyPassword = $storePassword
}
else {
    if (-not [string]::IsNullOrWhiteSpace($StorePasswordFile)) {
        $storePassword = Read-PasswordFile -Path $StorePasswordFile -Description 'keystore password'
    }
    else {
        $storePassword = Read-ConfirmedPassword -Prompt "Keystore password for $Alias"
    }

    if (-not [string]::IsNullOrWhiteSpace($KeyPasswordFile)) {
        $keyPassword = Read-PasswordFile -Path $KeyPasswordFile -Description 'key password'
    }
    else {
        $keyPassword = Read-ConfirmedPassword -Prompt "Key password for $Alias"
    }
}
if ($storePassword.Length -lt 6) { throw 'The keystore password must be at least 6 characters.' }
if ($keyPassword.Length -lt 6) { throw 'The key password must be at least 6 characters.' }

if (-not [string]::IsNullOrWhiteSpace($GoogleServicesJson)) {
    $GoogleServicesJson = Resolve-InputPath $GoogleServicesJson
}
else {
    foreach ($candidate in @((Join-Path $script:LocalRoot 'google-services.json'), (Join-Path $RepositoryRoot 'src\Errandesk.Client\Platforms\Android\google-services.json'))) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $GoogleServicesJson = $candidate; break }
    }
}
if (-not [string]::IsNullOrWhiteSpace($GoogleServicesJson) -and -not (Test-Path -LiteralPath $GoogleServicesJson -PathType Leaf)) {
    throw "google-services.json does not exist: $GoogleServicesJson"
}
$googleServicesSetting = if ([string]::IsNullOrWhiteSpace($GoogleServicesJson)) { 'google-services.json' } else { Get-SettingsPath $GoogleServicesJson }
if ([string]::IsNullOrWhiteSpace($GoogleServicesJson)) {
    Write-Warning 'google-services.json was not found. Place it in the local root, or pass -SkipFirebase when building without Firebase.'
}

$keystoreDirectory = Split-Path -Parent $keystorePath
if (-not [string]::IsNullOrWhiteSpace($keystoreDirectory)) { New-Item -ItemType Directory -Force -Path $keystoreDirectory | Out-Null }
New-Item -ItemType Directory -Force -Path $secretsDirectory | Out-Null

$keytoolArguments = @(
    '-genkeypair',
    '-v',
    '-keystore', $keystorePath,
    '-alias', $Alias,
    '-keyalg', 'RSA',
    '-keysize', '2048',
    '-validity', '10000',
    '-storepass:env', 'ERRANDESK_KEYTOOL_STORE_PASSWORD',
    '-keypass:env', 'ERRANDESK_KEYTOOL_KEY_PASSWORD',
    '-dname', 'CN=Errandesk'
)
if ($storePassword -cne $keyPassword) { $keytoolArguments += @('-storetype', 'JKS') }

# keytool reads the passwords from environment variables so that they never appear in the process arguments.
$env:ERRANDESK_KEYTOOL_STORE_PASSWORD = $storePassword
$env:ERRANDESK_KEYTOOL_KEY_PASSWORD = $keyPassword
try { Invoke-NativeCommand -FilePath $keytool -Arguments $keytoolArguments -Description "Creating the Android keystore for alias $Alias" }
finally { Remove-Item Env:\ERRANDESK_KEYTOOL_STORE_PASSWORD, Env:\ERRANDESK_KEYTOOL_KEY_PASSWORD -ErrorAction SilentlyContinue }

[System.IO.File]::WriteAllText($storePasswordPath, "$storePassword`n", (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($keyPasswordPath, "$keyPassword`n", (New-Object System.Text.UTF8Encoding($false)))

$settingsContent = @(
    '# Errandesk Android release signing.',
    "ERRANDESK_ANDROID_KEYSTORE=$(Get-SettingsPath $keystorePath)",
    "ERRANDESK_ANDROID_KEY_ALIAS=$Alias",
    "ERRANDESK_ANDROID_KEYSTORE_PASSWORD_FILE=$(Get-SettingsPath $storePasswordPath)",
    "ERRANDESK_ANDROID_KEY_PASSWORD_FILE=$(Get-SettingsPath $keyPasswordPath)",
    "ERRANDESK_ANDROID_GOOGLE_SERVICES_JSON=$googleServicesSetting",
    'ERRANDESK_ANDROID_OUTPUT_DIRECTORY=.'
) -join "`n"
[System.IO.File]::WriteAllText($settingsPath, $settingsContent + "`n", (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host "Keystore: $keystorePath"
Write-Host "Settings: $settingsPath"
Write-Host "Keystore password: $storePasswordPath"
Write-Host "Key password: $keyPasswordPath"
if (-not [string]::IsNullOrWhiteSpace($GoogleServicesJson)) { Write-Host "Firebase: $GoogleServicesJson" }
Write-Host 'Keep the keystore and password files. Losing them prevents updates to installed apps.'

Exit-Script 0
