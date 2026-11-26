#Requires -Version 5.1
<#
.SYNOPSIS
Builds a signed Errandesk Android APK into the output folder.

.DESCRIPTION
Reads signing and Firebase settings from an environment file, environment variables, or
parameters, then publishes the Release Android client with Firebase Cloud Messaging enabled
and copies the signed APK and build information into the output folder. Pass -SkipFirebase
to build without google-services.json and Firebase Cloud Messaging.

The key alias password and the keystore password can be configured separately. When the
keystore password is unset, the key password is used for both.

.PARAMETER RepositoryRoot
Errandesk repository root. Defaults to the app repository that contains this scripts
folder, or to the Errandesk folder next to a separate build repository.

.PARAMETER Configuration
Build configuration. Defaults to Release.

.PARAMETER Version
Application display version, for example 1.2.3. Defaults to the Version property in Directory.Build.props.

.PARAMETER VersionCode
Android version code. Defaults to major * 10000 + minor * 100 + patch from the display version.
The calculated code is unique only while minor and patch stay below 100; pass -VersionCode
explicitly for larger values.

.PARAMETER OutputDirectory
Folder that receives the APK and build-info.txt. Defaults to ERRANDESK_ANDROID_OUTPUT_DIRECTORY
from the environment or the settings file, then to the local root: the scripts\local folder
inside the app repository, or the build repository root.

.PARAMETER SettingsFile
Environment file with NAME=VALUE lines. Defaults to ERRANDESK_ANDROID_SETTINGS_FILE,
then errandesk-release.env in the local root.

.PARAMETER Keystore
Signing keystore file. Defaults to ERRANDESK_ANDROID_KEYSTORE or ERRANDESK_CLIENT_ANDROID_KEYSTORE.

.PARAMETER KeyAlias
Signing key alias. Defaults to ERRANDESK_ANDROID_KEY_ALIAS or ERRANDESK_CLIENT_ANDROID_KEY_ALIAS.

.PARAMETER GoogleServicesJson
Firebase google-services.json. Defaults to ERRANDESK_CLIENT_GOOGLE_SERVICES_JSON or
src\Errandesk.Client\Platforms\Android\google-services.json.

.PARAMETER SkipFirebase
Builds without google-services.json and without Firebase Cloud Messaging. The client
falls back to local reminder alarms.

.PARAMETER ApplicationId
Android package name. Defaults to com.airtaxi.errandesk.

.PARAMETER Bundle
Also produce an Android App Bundle (.aab) for Play Console uploads.

.PARAMETER Install
Install the built APK on the connected device with adb install -r.

.EXAMPLE
.\build-android.ps1

.EXAMPLE
.\build-android.ps1 -Version 1.0.0 -VersionCode 10000 -Bundle -Install
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [string] $Configuration = 'Release',
    [string] $Version,
    [int] $VersionCode = 0,
    [string] $OutputDirectory,
    [string] $SettingsFile,
    [string] $Keystore,
    [string] $KeyAlias,
    [string] $GoogleServicesJson,
    [string] $ApplicationId = 'com.airtaxi.errandesk',
    [switch] $Bundle,
    [switch] $Install,
    [switch] $SkipFirebase
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

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $script:LocalRoot }
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = $defaultRepositoryRoot }
$RepositoryRoot = Resolve-InputPath $RepositoryRoot
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

if (-not $PSBoundParameters.ContainsKey('SettingsFile')) {
    $SettingsFile = Resolve-Setting -Settings @{} -Names @('ERRANDESK_ANDROID_SETTINGS_FILE') -Default (Join-Path $script:LocalRoot 'errandesk-release.env')
}
$SettingsFile = Resolve-InputPath $SettingsFile
if ($PSBoundParameters.ContainsKey('SettingsFile') -and -not (Test-Path -LiteralPath $SettingsFile -PathType Leaf)) {
    throw "The settings file does not exist: $SettingsFile"
}

$settings = Read-SettingsFile -Path $SettingsFile

if (-not $PSBoundParameters.ContainsKey('Keystore')) {
    $Keystore = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_KEYSTORE', 'ERRANDESK_CLIENT_ANDROID_KEYSTORE') -Default $null
}
if (-not $PSBoundParameters.ContainsKey('KeyAlias')) {
    $KeyAlias = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_KEY_ALIAS', 'ERRANDESK_CLIENT_ANDROID_KEY_ALIAS') -Default $null
}
if (-not $SkipFirebase -and -not $PSBoundParameters.ContainsKey('GoogleServicesJson')) {
    $GoogleServicesJson = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_GOOGLE_SERVICES_JSON', 'ERRANDESK_CLIENT_GOOGLE_SERVICES_JSON') -Default (Join-Path $RepositoryRoot 'src\Errandesk.Client\Platforms\Android\google-services.json')
}
if (-not $PSBoundParameters.ContainsKey('OutputDirectory')) {
    $OutputDirectory = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_OUTPUT_DIRECTORY') -Default $OutputDirectory
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_VERSION') -Default $null
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    $buildPropertiesPath = Join-Path $RepositoryRoot 'Directory.Build.props'
    if (Test-Path -LiteralPath $buildPropertiesPath -PathType Leaf) {
        $versionMatch = [regex]::Match((Get-Content -LiteralPath $buildPropertiesPath -Raw), '<Version>\s*([^<\s]+)\s*</Version>')
        if ($versionMatch.Success) { $Version = $versionMatch.Groups[1].Value }
    }
}
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = '0.1.0' }

if ($VersionCode -le 0) {
    $versionCodeValue = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_VERSION_CODE') -Default $null
    if (-not [string]::IsNullOrWhiteSpace($versionCodeValue)) { $VersionCode = [int]$versionCodeValue }
}
if ($VersionCode -le 0) {
    $versionMatch = [regex]::Match($Version, '^(\d+)\.(\d+)(?:\.(\d+))?')
    if ($versionMatch.Success) {
        $major = [int]$versionMatch.Groups[1].Value
        $minor = [int]$versionMatch.Groups[2].Value
        $patch = if ($versionMatch.Groups[3].Success) { [int]$versionMatch.Groups[3].Value } else { 0 }
        $VersionCode = [Math]::Max(1, ($major * 10000) + ($minor * 100) + $patch)
    }
    else { $VersionCode = 1 }
}

if ([string]::IsNullOrWhiteSpace($Keystore)) { throw 'Set ERRANDESK_ANDROID_KEYSTORE in the settings file, in the environment, or with -Keystore.' }
if ([string]::IsNullOrWhiteSpace($KeyAlias)) { throw 'Set ERRANDESK_ANDROID_KEY_ALIAS in the settings file, in the environment, or with -KeyAlias.' }
$Keystore = Resolve-InputPath $Keystore
$OutputDirectory = Resolve-InputPath $OutputDirectory
if (-not (Test-Path -LiteralPath $Keystore -PathType Leaf)) { throw "The keystore does not exist: $Keystore" }
if (-not $SkipFirebase) {
    $GoogleServicesJson = Resolve-InputPath $GoogleServicesJson
    if (-not (Test-Path -LiteralPath $GoogleServicesJson -PathType Leaf)) { throw "google-services.json does not exist: $GoogleServicesJson" }
}

$keystorePath = (Resolve-Path -LiteralPath $Keystore).Path
$googleServicesPath = $null
if (-not $SkipFirebase) { $googleServicesPath = (Resolve-Path -LiteralPath $GoogleServicesJson).Path }
$projectPath = Join-Path $RepositoryRoot 'src\Errandesk.Client\Errandesk.Client.csproj'
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { throw "The client project does not exist: $projectPath" }

$firebaseProjectId = $null
if (-not $SkipFirebase) {
    $googleServices = Get-Content -LiteralPath $googleServicesPath -Raw | ConvertFrom-Json
    $registeredPackages = @()
    foreach ($client in @($googleServices.client)) {
        $packageName = $client.client_info.android_client_info.package_name
        if (-not [string]::IsNullOrWhiteSpace($packageName)) { $registeredPackages += $packageName }
    }
    if ($registeredPackages -notcontains $ApplicationId) {
        throw "google-services.json registers [$($registeredPackages -join ', ')], not $ApplicationId. Register the package in Firebase or pass -ApplicationId."
    }
    $firebaseProjectId = $googleServices.project_info.project_id
}

$keyPassword = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_KEY_PASSWORD') -Default $null
if ([string]::IsNullOrWhiteSpace($keyPassword)) {
    $keyPasswordSourceFile = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_KEY_PASSWORD_FILE', 'ERRANDESK_CLIENT_ANDROID_KEY_PASSWORD_FILE') -Default $null
    if (-not [string]::IsNullOrWhiteSpace($keyPasswordSourceFile)) {
        $keyPasswordSourceFile = Resolve-InputPath $keyPasswordSourceFile
        if (-not (Test-Path -LiteralPath $keyPasswordSourceFile -PathType Leaf)) { throw "The key password file does not exist: $keyPasswordSourceFile" }
        $keyPassword = (Get-Content -LiteralPath $keyPasswordSourceFile -Raw).Trim()
    }
}
if ([string]::IsNullOrWhiteSpace($keyPassword)) { $keyPassword = Read-PlainTextPassword -Prompt "Key password for $KeyAlias" }

$storePassword = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_KEYSTORE_PASSWORD', 'ERRANDESK_ANDROID_STORE_PASSWORD') -Default $null
if ([string]::IsNullOrWhiteSpace($storePassword)) {
    $storePasswordSourceFile = Resolve-Setting -Settings $settings -Names @('ERRANDESK_ANDROID_KEYSTORE_PASSWORD_FILE', 'ERRANDESK_ANDROID_STORE_PASSWORD_FILE', 'ERRANDESK_CLIENT_ANDROID_KEYSTORE_PASSWORD_FILE') -Default $null
    if (-not [string]::IsNullOrWhiteSpace($storePasswordSourceFile)) {
        $storePasswordSourceFile = Resolve-InputPath $storePasswordSourceFile
        if (-not (Test-Path -LiteralPath $storePasswordSourceFile -PathType Leaf)) { throw "The keystore password file does not exist: $storePasswordSourceFile" }
        $storePassword = (Get-Content -LiteralPath $storePasswordSourceFile -Raw).Trim()
    }
}
if ([string]::IsNullOrWhiteSpace($storePassword)) { $storePassword = $keyPassword }

$keytool = Get-Command keytool.exe -ErrorAction SilentlyContinue
if (-not $keytool) { $keytool = Get-Command keytool -ErrorAction SilentlyContinue }
if ($keytool) {
    # keytool reads the password from an environment variable so that it never appears in the process arguments.
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $env:ERRANDESK_KEYTOOL_STORE_PASSWORD = $storePassword
    try {
        & $keytool.Source -list -keystore $keystorePath '-storepass:env' 'ERRANDESK_KEYTOOL_STORE_PASSWORD' -alias $KeyAlias *> $null
        $keytoolExitCode = $LASTEXITCODE
    }
    finally {
        Remove-Item Env:\ERRANDESK_KEYTOOL_STORE_PASSWORD -ErrorAction SilentlyContinue
        $ErrorActionPreference = $previousErrorAction
    }
    if ($keytoolExitCode -ne 0) { throw "The keystore cannot be opened with alias $KeyAlias and the supplied keystore password." }
}
else {
    Write-Warning 'keytool was not found, so the keystore was not checked before the build.'
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

$outDirectory = Join-Path $RepositoryRoot "src\Errandesk.Client\bin\$Configuration\net11.0-android"
$publishDirectory = Join-Path $outDirectory 'publish'
foreach ($packageDirectory in @($outDirectory, $publishDirectory)) {
    if (Test-Path -LiteralPath $packageDirectory -PathType Container) {
        Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Extension -in '.apk', '.aab' } | Remove-Item -Force
    }
}

$environmentNames = @(
    'ERRANDESK_CLIENT_ANDROID_KEYSTORE',
    'ERRANDESK_CLIENT_ANDROID_KEY_ALIAS',
    'ERRANDESK_CLIENT_ANDROID_KEY_PASSWORD_FILE',
    'ERRANDESK_CLIENT_ANDROID_KEYSTORE_PASSWORD_FILE',
    'ERRANDESK_CLIENT_APPLICATION_ID',
    'ERRANDESK_CLIENT_GOOGLE_SERVICES_JSON'
)
$previousEnvironmentValues = $null
$keyPasswordFile = $null
$storePasswordFile = $null

try {
    $previousEnvironmentValues = @{}
    foreach ($name in $environmentNames) {
        $previousEnvironmentValues[$name] = [Environment]::GetEnvironmentVariable($name)
    }

    $tempId = [Guid]::NewGuid().ToString('N')
    $keyPasswordFile = Join-Path ([System.IO.Path]::GetTempPath()) ("errandesk-android-key-{0}.txt" -f $tempId)
    $storePasswordFile = Join-Path ([System.IO.Path]::GetTempPath()) ("errandesk-android-store-{0}.txt" -f $tempId)
    [System.IO.File]::WriteAllText($keyPasswordFile, "$keyPassword`n", (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($storePasswordFile, "$storePassword`n", (New-Object System.Text.UTF8Encoding($false)))

    $env:ERRANDESK_CLIENT_ANDROID_KEYSTORE = $keystorePath
    $env:ERRANDESK_CLIENT_ANDROID_KEY_ALIAS = $KeyAlias
    $env:ERRANDESK_CLIENT_ANDROID_KEY_PASSWORD_FILE = $keyPasswordFile
    $env:ERRANDESK_CLIENT_ANDROID_KEYSTORE_PASSWORD_FILE = $storePasswordFile
    $env:ERRANDESK_CLIENT_APPLICATION_ID = $ApplicationId
    if ($SkipFirebase) { Remove-Item Env:\ERRANDESK_CLIENT_GOOGLE_SERVICES_JSON -ErrorAction SilentlyContinue }
    else { $env:ERRANDESK_CLIENT_GOOGLE_SERVICES_JSON = $googleServicesPath }

    $packageFormats = if ($Bundle) { 'apk;aab' } else { 'apk' }
    $arguments = @(
        'publish', $projectPath,
        '-f', 'net11.0-android',
        '-c', $Configuration,
        "-p:Version=$Version",
        "-p:ApplicationVersion=$VersionCode",
        "-p:AndroidPackageFormats=$packageFormats",
        '-p:ErrandeskIncludeIosTarget=false',
        '-nologo',
        '-v', 'minimal'
    )

    if ($SkipFirebase) { Write-Host "Building Errandesk $Version ($VersionCode) for Android without Firebase." }
    else { Write-Host "Building Errandesk $Version ($VersionCode) for Android with Firebase project $firebaseProjectId." }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}
finally {
    if ($null -ne $previousEnvironmentValues) {
        foreach ($name in $environmentNames) {
            $previousValue = $previousEnvironmentValues[$name]
            if ([string]::IsNullOrEmpty($previousValue)) {
                [Environment]::SetEnvironmentVariable($name, $null)
            }
            else {
                [Environment]::SetEnvironmentVariable($name, $previousValue)
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($keyPasswordFile)) { Remove-Item -LiteralPath $keyPasswordFile -Force -ErrorAction SilentlyContinue }
    if (-not [string]::IsNullOrWhiteSpace($storePasswordFile)) { Remove-Item -LiteralPath $storePasswordFile -Force -ErrorAction SilentlyContinue }
}

$apks = @(Get-ChildItem -LiteralPath $publishDirectory -File -Filter '*.apk' -ErrorAction SilentlyContinue)
$signedApks = @($apks | Where-Object { $_.Name -match '(?i)-signed\.apk$' })
if ($signedApks.Count -eq 0) { throw "No signed APK was produced in $publishDirectory. Check the signing settings." }
$apks = $signedApks

$copiedApks = @()
foreach ($apk in $apks) {
    $abi = ''
    foreach ($candidate in @('arm64-v8a', 'armeabi-v7a', 'x86_64', 'x86')) {
        if ($apk.Name.Contains($candidate)) { $abi = "-$candidate"; break }
    }
    if ($apks.Count -eq 1) { $targetName = "Errandesk-$Version.apk" }
    else { $targetName = "Errandesk-$Version$abi.apk" }

    $targetPath = Join-Path $OutputDirectory $targetName
    Copy-Item -LiteralPath $apk.FullName -Destination $targetPath -Force
    $copiedApks += $targetPath
}

$latestPath = Join-Path $OutputDirectory 'Errandesk-latest.apk'
if ($copiedApks.Count -eq 1) { Copy-Item -LiteralPath $copiedApks[0] -Destination $latestPath -Force }

$copiedBundles = @()
foreach ($bundle in @(Get-ChildItem -LiteralPath $publishDirectory -File -Filter '*.aab' -ErrorAction SilentlyContinue)) {
    $bundlePath = Join-Path $OutputDirectory "Errandesk-$Version.aab"
    Copy-Item -LiteralPath $bundle.FullName -Destination $bundlePath -Force
    $copiedBundles += $bundlePath
}

$lines = @(
    "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
    "Version: $Version",
    "Version code: $VersionCode",
    "Package: $ApplicationId",
    $(if ($SkipFirebase) { 'Firebase: skipped' } else { "Firebase project: $firebaseProjectId" }),
    "Configuration: $Configuration",
    ''
)
foreach ($apkPath in $copiedApks) {
    $file = Get-Item -LiteralPath $apkPath
    $lines += "APK: $($file.FullName)"
    $lines += ("Size: {0:N1} MB" -f ($file.Length / 1MB))
    $lines += "SHA-256: $((Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash)"
    $lines += ''
}
foreach ($bundlePath in $copiedBundles) {
    $file = Get-Item -LiteralPath $bundlePath
    $lines += "AAB: $($file.FullName)"
    $lines += ("Size: {0:N1} MB" -f ($file.Length / 1MB))
    $lines += "SHA-256: $((Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash)"
    $lines += ''
}
[System.IO.File]::WriteAllLines((Join-Path $OutputDirectory 'build-info.txt'), [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
foreach ($apkPath in $copiedApks) {
    $file = Get-Item -LiteralPath $apkPath
    Write-Host ("APK:    {0} ({1:N1} MB)" -f $file.FullName, ($file.Length / 1MB))
}
foreach ($bundlePath in $copiedBundles) {
    $file = Get-Item -LiteralPath $bundlePath
    Write-Host ("AAB:    {0} ({1:N1} MB)" -f $file.FullName, ($file.Length / 1MB))
}
Write-Host "Package: $ApplicationId"
Write-Host "Version: $Version ($VersionCode)"
if ($SkipFirebase) { Write-Host 'Firebase: skipped' }
else { Write-Host "Firebase: $firebaseProjectId" }
Write-Host "Info:   $(Join-Path $OutputDirectory 'build-info.txt')"

if ($Install) {
    $adbPath = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
    if (-not (Test-Path -LiteralPath $adbPath -PathType Leaf)) {
        $adbCommand = Get-Command adb.exe -ErrorAction SilentlyContinue
        $adbPath = if ($adbCommand) { $adbCommand.Source } else { $null }
    }
    if ([string]::IsNullOrWhiteSpace($adbPath)) { throw 'adb was not found.' }
    & $adbPath install -r $copiedApks[0]
    if ($LASTEXITCODE -ne 0) { throw "adb install failed with exit code $LASTEXITCODE." }
    Write-Host 'Installed on the connected device.'
}

# Keep the console open when the script was started by a double-click.
if (Test-LaunchedFromFileManager) {
    $null = Read-Host 'Press Enter to close this window'
}
