#Requires -Version 5.1
<#
.SYNOPSIS
Builds a signed Errandesk Windows MSIX bundle.

.DESCRIPTION
Reads the version from Directory.Build.props, builds the Windows client for every requested
architecture, packs one MSIX per architecture, signs the packages and the bundle, and copies
the bundle and its code-signing certificate into the output folder.

The MSIX package version is the declared version completed to four parts, because the package
manifest requires four. A declared 0.1.0 becomes 0.1.0.0, and a declared 0.1.0.5 becomes 0.1.0.5.

After the bundle is signed, aticmsixgen turns it into one EXE installer per architecture. The
installers are written to the output folder as Installer-arm64.exe, Installer-x64.exe, and so on.
Pass -SkipInstallers to produce only the MSIX bundle.

The first run creates a self-signed code-signing certificate whose subject is the
WindowsPublisher value, which matches the package publisher generated from ApplicationPublisher.
The certificate is exported to the PFX and CER files and then removed from the user certificate
store. An existing settings file is never rewritten.

.PARAMETER RepositoryRoot
Errandesk repository root. Defaults to the app repository that contains this scripts
folder, or to the Errandesk folder next to a separate build repository.

.PARAMETER Configuration
Build configuration. Defaults to Release.

.PARAMETER Architectures
Windows architectures to bundle. Defaults to arm64 and x64. Accepts a PowerShell array or a
comma-separated string such as arm64,x64.

.PARAMETER Revision
Fourth part of the MSIX package version. Overrides the fourth part in Directory.Build.props,
which defaults to 0 when that version declares only three parts.

.PARAMETER OutputDirectory
Folder that receives the bundle, certificate and build information. Defaults to the
local root: the scripts\local folder inside the app repository, or the build repository root.

.PARAMETER SettingsFile
Environment file with the signing certificate path and password. Defaults to
windows-signing.env in the local root.

.PARAMETER KeepIntermediate
Keeps the staging, per-architecture package and build log folders.

.PARAMETER SkipInstallers
Skips the per-architecture EXE installers that aticmsixgen builds from the bundle.

.EXAMPLE
.\build-windows-msix.ps1

.EXAMPLE
.\build-windows-msix.ps1 -Architectures arm64,x64 -Revision 2

.EXAMPLE
.\build-windows-msix.ps1 -SkipInstallers
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [string] $Configuration = 'Release',
    [string[]] $Architectures = @('arm64', 'x64'),
    [int] $Revision = -1,
    [string] $OutputDirectory,
    [string] $SettingsFile,
    [switch] $KeepIntermediate,
    [switch] $SkipInstallers
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')

# Certificate subject and package publisher. It must match ApplicationPublisher in the client project.
$script:WindowsPublisher = 'O=Howon Lee'

# The .cmd wrappers pass -Architectures arm64,x64 as one string, so split comma-separated values.
$Architectures = @($Architectures | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Architectures.Count -eq 0) { throw 'Specify at least one architecture.' }

trap {
    Write-Host ''
    Write-Host "The build failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage -ForegroundColor Red
    if (Test-LaunchedFromFileManager) {
        $null = Read-Host 'Press Enter to close this window'
    }
    exit 1
}

function Get-WindowsKitTool {
    param([string] $Name)

    $windowsKitRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $preferred = Join-Path $windowsKitRoot "10.0.26100.0\x64\$Name"
    if (Test-Path -LiteralPath $preferred -PathType Leaf) { return $preferred }

    $candidate = Get-ChildItem -Path $windowsKitRoot -Recurse -Filter $Name -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($candidate) { return $candidate.FullName }
    throw "$Name was not found under $windowsKitRoot. Install the Windows SDK."
}

function Invoke-NativeCommand {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [string] $Description,
        [string] $LogPath,
        [switch] $Quiet
    )

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $FilePath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorAction
    if ($LogPath) { $output | Set-Content -LiteralPath $LogPath -Encoding UTF8 }
    if ($exitCode -ne 0) {
        $output | Out-Host
        throw "$Description failed with exit code $exitCode."
    }
    if (-not $Quiet) { $output | Out-Host }
}

function Resolve-AticMsixGen {
    $command = Get-Command aticmsixgen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }

    foreach ($name in @('aticmsixgen.exe', 'aticmsixgen.cmd')) {
        $globalToolPath = Join-Path $env:USERPROFILE ".dotnet\tools\$name"
        if (Test-Path -LiteralPath $globalToolPath -PathType Leaf) { return $globalToolPath }
    }
    throw 'aticmsixgen was not found. Install it with "dotnet tool install --global aticmsixgen", or pass -SkipInstallers.'
}

function New-ExeInstallers {
    param(
        [string] $BundlePath,
        [string[]] $PackageArchitectures
    )

    $aticMsixGen = Resolve-AticMsixGen
    $workDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("errandesk-installers-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $workDirectory | Out-Null
    try {
        Write-Host 'Building the EXE installers with aticmsixgen.'
        $logPath = Join-Path $logsDirectory 'installers.log'
        if ($PackageArchitectures.Count -eq 1) {
            $archivePath = Join-Path $workDirectory "Installer-$($PackageArchitectures[0]).exe"
            Invoke-NativeCommand -FilePath $aticMsixGen -Arguments @('exe-pack', '-i', $BundlePath, '-o', $archivePath) -Description 'Building the EXE installer' -LogPath $logPath -Quiet
        }
        else {
            $archivePath = Join-Path $workDirectory 'Installers.zip'
            Invoke-NativeCommand -FilePath $aticMsixGen -Arguments @('exe-pack', '-i', $BundlePath, '-o', $archivePath) -Description 'Building the EXE installers' -LogPath $logPath -Quiet
            Expand-Archive -LiteralPath $archivePath -DestinationPath $workDirectory -Force
        }

        $installerPaths = @()
        foreach ($architecture in $PackageArchitectures) {
            $name = "Installer-$architecture.exe"
            $sourcePath = Join-Path $workDirectory $name
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "aticmsixgen did not produce $name." }
            $destinationPath = Join-Path $OutputDirectory $name
            Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
            $installerPaths += $destinationPath
        }
        return $installerPaths
    }
    finally {
        Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $script:LocalRoot }
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = $defaultRepositoryRoot }
$RepositoryRoot = Resolve-InputPath $RepositoryRoot
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$OutputDirectory = Resolve-InputPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

if ([string]::IsNullOrWhiteSpace($SettingsFile)) { $SettingsFile = Join-Path $script:LocalRoot 'windows-signing.env' }
$SettingsFile = Resolve-InputPath $SettingsFile

$propertiesPath = Join-Path $RepositoryRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $propertiesPath -PathType Leaf)) { throw "Directory.Build.props does not exist: $propertiesPath" }
$versionMatch = [regex]::Match((Get-Content -LiteralPath $propertiesPath -Raw), '<Version>\s*([^<]+?)\s*</Version>')
if (-not $versionMatch.Success) { throw "Directory.Build.props has no Version element: $propertiesPath" }

$declaredVersion = $versionMatch.Groups[1].Value
if ($declaredVersion -notmatch '^\d+(\.\d+){1,3}$') { throw "The Version value is not numeric: $declaredVersion" }
$versionParts = @($declaredVersion.Split('.') | ForEach-Object { [int]$_ })
$major = $versionParts[0]
$minor = if ($versionParts.Count -gt 1) { $versionParts[1] } else { 0 }
$patch = if ($versionParts.Count -gt 2) { $versionParts[2] } else { 0 }
$packageRevision = if ($versionParts.Count -gt 3) { $versionParts[3] } else { 0 }
if ($PSBoundParameters.ContainsKey('Revision')) {
    if ($Revision -lt 0 -or $Revision -gt 65535) { throw 'Revision must be between 0 and 65535.' }
    $packageRevision = $Revision
}
foreach ($part in @($major, $minor, $patch, $packageRevision)) {
    if ($part -lt 0 -or $part -gt 65535) { throw "Every version part must be between 0 and 65535: $declaredVersion" }
}
$displayVersion = "$major.$minor.$patch"
$packageVersion = "$displayVersion.$packageRevision"

$projectPath = Join-Path $RepositoryRoot 'src\Errandesk.Client\Errandesk.Client.csproj'
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { throw "The client project does not exist: $projectPath" }
$binRoot = Join-Path $RepositoryRoot 'src\Errandesk.Client\bin'
$configurationBinRoot = Join-Path $binRoot $Configuration

$makeAppx = Get-WindowsKitTool 'makeappx.exe'
$signTool = Get-WindowsKitTool 'signtool.exe'

$settings = Read-SettingsFile -Path $SettingsFile
$pfxPath = Resolve-InputPath (Resolve-Setting -Settings $settings -Names @('ERRANDESK_WINDOWS_PFX') -Default (Join-Path $OutputDirectory 'Errandesk-Windows.pfx'))
$cerPath = Resolve-InputPath (Resolve-Setting -Settings $settings -Names @('ERRANDESK_WINDOWS_CER') -Default (Join-Path $OutputDirectory 'Errandesk-Windows.cer'))
$passwordFile = Resolve-InputPath (Resolve-Setting -Settings $settings -Names @('ERRANDESK_WINDOWS_PFX_PASSWORD_FILE') -Default $null)
$pfxPassword = Resolve-Setting -Settings $settings -Names @('ERRANDESK_WINDOWS_PFX_PASSWORD') -Default $null
if ([string]::IsNullOrWhiteSpace($pfxPassword) -and -not [string]::IsNullOrWhiteSpace($passwordFile) -and (Test-Path -LiteralPath $passwordFile -PathType Leaf)) {
    $pfxPassword = (Get-Content -LiteralPath $passwordFile -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($pfxPassword)) { throw "The signing password file is empty: $passwordFile" }
}

if (-not (Test-Path -LiteralPath $pfxPath -PathType Leaf)) {
    # A generated password goes to the configured password file, or to secrets\windows-pfx-password.txt when none is configured.
    $generatedPasswordFile = $null
    if ([string]::IsNullOrWhiteSpace($pfxPassword)) {
        $pfxPassword = [Guid]::NewGuid().ToString('N')
        $generatedPasswordFile = if ([string]::IsNullOrWhiteSpace($passwordFile)) { Join-Path $script:LocalRoot 'secrets\windows-pfx-password.txt' } else { $passwordFile }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $generatedPasswordFile) | Out-Null
        [System.IO.File]::WriteAllText($generatedPasswordFile, "$pfxPassword`n", (New-Object System.Text.UTF8Encoding($false)))
    }

    Write-Host 'Creating the Windows code-signing certificate.'
    $storeCertificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $script:WindowsPublisher `
        -FriendlyName 'Errandesk Windows MSIX' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(10)
    try {
        foreach ($exportPath in @($pfxPath, $cerPath)) { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $exportPath) | Out-Null }
        $securePassword = ConvertTo-SecureString -String $pfxPassword -AsPlainText -Force
        Export-PfxCertificate -Cert $storeCertificate -FilePath $pfxPath -Password $securePassword | Out-Null
        Export-Certificate -Cert $storeCertificate -FilePath $cerPath -Type CERT | Out-Null
    }
    finally {
        # The exported PFX holds the only copy of the private key; the user certificate store keeps neither the certificate nor the key.
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($storeCertificate.Thumbprint)" -DeleteKey -ErrorAction SilentlyContinue
    }

    # An existing settings file is never rewritten.
    if (Test-Path -LiteralPath $SettingsFile -PathType Leaf) {
        if ($generatedPasswordFile -and [string]::IsNullOrWhiteSpace($passwordFile)) {
            Write-Warning "The settings file was left unchanged. Add ERRANDESK_WINDOWS_PFX_PASSWORD_FILE=$(Get-SettingsPath $generatedPasswordFile) to $SettingsFile."
        }
    }
    else {
        $settingsLines = @(
            '# Errandesk Windows MSIX signing.',
            "ERRANDESK_WINDOWS_PFX=$(Get-SettingsPath $pfxPath)"
        )
        $settingsPasswordFile = if ($generatedPasswordFile) { $generatedPasswordFile } else { $passwordFile }
        if (-not [string]::IsNullOrWhiteSpace($settingsPasswordFile)) { $settingsLines += "ERRANDESK_WINDOWS_PFX_PASSWORD_FILE=$(Get-SettingsPath $settingsPasswordFile)" }
        $settingsLines += "ERRANDESK_WINDOWS_CER=$(Get-SettingsPath $cerPath)"
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SettingsFile) | Out-Null
        [System.IO.File]::WriteAllText($SettingsFile, ($settingsLines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
    }
}
elseif ([string]::IsNullOrWhiteSpace($pfxPassword)) {
    if (-not [string]::IsNullOrWhiteSpace($passwordFile)) { throw "The signing password file does not exist: $passwordFile" }
    throw "The signing password is missing. Set ERRANDESK_WINDOWS_PFX_PASSWORD or ERRANDESK_WINDOWS_PFX_PASSWORD_FILE, or delete $pfxPath to create a new certificate."
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxPath, $pfxPassword)
if ($certificate.Subject -ne $script:WindowsPublisher) { throw "The signing certificate subject is not $($script:WindowsPublisher): $($certificate.Subject)" }
if (-not (Test-Path -LiteralPath $cerPath -PathType Leaf)) {
    [System.IO.File]::WriteAllBytes($cerPath, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
}

$stagingRoot = Join-Path $OutputDirectory 'windows-staging'
$packagesDirectory = Join-Path $OutputDirectory 'windows-packages'
$logsDirectory = Join-Path $OutputDirectory 'windows-logs'
foreach ($directory in @($stagingRoot, $packagesDirectory, $logsDirectory)) {
    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

function Build-Architecture {
    param([string] $Architecture)

    Write-Host "Building Errandesk $displayVersion for Windows $Architecture."
    $architectureBinRoot = Join-Path $binRoot "$Architecture\$Configuration"
    if (Test-Path -LiteralPath $configurationBinRoot) {
        Get-ChildItem -Path $configurationBinRoot -Recurse -Directory -Filter 'AppX' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $architectureBinRoot) {
        Get-ChildItem -Path $architectureBinRoot -Recurse -Directory -Filter "win-$Architecture" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }

    $buildArguments = @(
        'build', $projectPath,
        '-f', 'net11.0-windows10.0.26100.0',
        '-c', $Configuration,
        "-p:Platform=$Architecture",
        "-p:RuntimeIdentifier=win-$Architecture",
        "-p:Version=$displayVersion",
        "-p:ApplicationVersion=$packageRevision",
        '-p:ErrandeskIncludeIosTarget=false',
        '-nologo',
        '-v', 'minimal'
    )
    $logPath = Join-Path $logsDirectory "build-$Architecture.log"
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & dotnet @buildArguments 2>&1 | Tee-Object -FilePath $logPath | Out-Host
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorAction
    if ($exitCode -ne 0) { throw "dotnet build failed for $Architecture. See $logPath." }

    $manifest = Get-ChildItem -Path $architectureBinRoot -Recurse -Filter 'AppxManifest.xml' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $manifest) {
        $manifest = Get-ChildItem -Path $binRoot -Recurse -Filter 'AppxManifest.xml' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -match "\\$Architecture\\$Configuration\\" } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
    }
    if (-not $manifest) { throw "No AppxManifest.xml was produced for $Architecture under $architectureBinRoot." }

    [xml] $manifestXml = Get-Content -LiteralPath $manifest.FullName -Raw
    $script:manifestXml = $manifestXml
    $identity = $manifestXml.Package.Identity
    if ($identity.ProcessorArchitecture -ne $Architecture) { throw "The generated manifest is for $($identity.ProcessorArchitecture), not $Architecture." }
    if ($identity.Publisher -ne $script:WindowsPublisher) { throw "The generated publisher is $($identity.Publisher), not $($script:WindowsPublisher)." }
    if ($identity.Version -ne $packageVersion) { throw "The generated package version is $($identity.Version), not $packageVersion." }

    $destination = Join-Path $stagingRoot $Architecture
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $manifest.Directory.FullName -Force | Copy-Item -Destination $destination -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path $destination 'AppxManifest.xml') -PathType Leaf)) { throw "Copying the $Architecture package layout failed." }
    return $destination
}

$packages = @()
foreach ($architecture in $Architectures) {
    if ($architecture -notin @('arm64', 'x64', 'x86')) { throw "Unsupported architecture: $architecture" }
    $layout = Build-Architecture -Architecture $architecture
    $packagePath = Join-Path $packagesDirectory ("Errandesk_{0}_{1}.msix" -f $packageVersion, $architecture)
    Write-Host "Packing and signing the $architecture MSIX."
    Invoke-NativeCommand -FilePath $makeAppx -Arguments @('pack', '/d', $layout, '/p', $packagePath, '/o') -Description "Packing $architecture MSIX" -LogPath (Join-Path $logsDirectory "pack-$architecture.log") -Quiet
    Invoke-NativeCommand -FilePath $signTool -Arguments @('sign', '/fd', 'SHA256', '/a', '/f', $pfxPath, '/p', $pfxPassword, $packagePath) -Description "Signing $architecture MSIX" -LogPath (Join-Path $logsDirectory "sign-$architecture.log") -Quiet
    $packages += $packagePath
}

$bundlePath = Join-Path $OutputDirectory ("Errandesk-{0}.msixbundle" -f $packageVersion)
Write-Host 'Bundling and signing the MSIX bundle.'
Invoke-NativeCommand -FilePath $makeAppx -Arguments @('bundle', '/d', $packagesDirectory, '/p', $bundlePath, '/o') -Description 'Bundling the MSIX packages' -LogPath (Join-Path $logsDirectory 'bundle.log') -Quiet
Invoke-NativeCommand -FilePath $signTool -Arguments @('sign', '/fd', 'SHA256', '/a', '/f', $pfxPath, '/p', $pfxPassword, $bundlePath) -Description 'Signing the MSIX bundle' -LogPath (Join-Path $logsDirectory 'sign-bundle.log') -Quiet

$verifyDirectory = Join-Path $OutputDirectory 'windows-verify'
if (Test-Path -LiteralPath $verifyDirectory) { Remove-Item -LiteralPath $verifyDirectory -Recurse -Force }
Invoke-NativeCommand -FilePath $makeAppx -Arguments @('unbundle', '/p', $bundlePath, '/d', $verifyDirectory) -Description 'Validating the MSIX bundle' -LogPath (Join-Path $logsDirectory 'unbundle.log') -Quiet
if (-not (Test-Path -LiteralPath (Join-Path $verifyDirectory 'AppxMetadata\AppxBundleManifest.xml') -PathType Leaf)) { throw 'The MSIX bundle manifest was not extracted.' }
Remove-Item -LiteralPath $verifyDirectory -Recurse -Force

$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$signatureOutput = & $signTool verify /v $bundlePath 2>&1
$signatureExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorAction
$signatureOutput | Write-Host
if ($signatureExitCode -ne 0) {
    Write-Warning 'The bundle is signed, but its self-signed certificate is not trusted on this machine. Install the CER into Local Machine > Trusted People before installing the bundle.'
}

$installerPaths = @()
if ($SkipInstallers) {
    Write-Host 'Skipping the EXE installers.'
}
else {
    $installerPaths = New-ExeInstallers -BundlePath $bundlePath -PackageArchitectures $Architectures
}

$bundleFile = Get-Item -LiteralPath $bundlePath
$bundleHash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash
$runtimeDependency = @($script:manifestXml.Package.Dependencies.PackageDependency) | Where-Object { $_.Name -like 'Microsoft.WindowsAppRuntime*' } | Select-Object -First 1
$runtimeDependencyText = if ($runtimeDependency) { "$($runtimeDependency.Name) $($runtimeDependency.MinVersion)" } else { 'None' }

$infoLines = @(
    "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
    "Display version: $displayVersion",
    "MSIX version: $packageVersion",
    "Package: com.airtaxi.errandesk",
    "Publisher: $($script:WindowsPublisher)",
    "Architectures: $($Architectures -join ', ')",
    "Windows App Runtime dependency: $runtimeDependencyText",
    "",
    "Bundle: $bundlePath",
    ("Size: {0:N1} MB" -f ($bundleFile.Length / 1MB)),
    "SHA-256: $bundleHash",
    ""
)

if ($SkipInstallers) {
    $infoLines += 'EXE installers: skipped'
    $infoLines += ''
}
else {
    $infoLines += 'EXE installers:'
    foreach ($installerPath in $installerPaths) {
        $installerFile = Get-Item -LiteralPath $installerPath
        $infoLines += "  $($installerFile.Name): $installerPath"
        $infoLines += ("  Size: {0:N1} MB" -f ($installerFile.Length / 1MB))
        $infoLines += "  SHA-256: $((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash)"
    }
    $infoLines += ''
}

$infoLines += @(
    "Certificate subject: $($certificate.Subject)",
    "Certificate thumbprint: $($certificate.Thumbprint)",
    "Certificate expires: $($certificate.NotAfter.ToString('yyyy-MM-dd'))",
    "Certificate file: $cerPath",
    "Signature: signed; the certificate must be trusted on the target PC before installing.",
    "",
    "Install the certificate into Local Machine > Trusted People before installing the bundle.",
    "PowerShell: Import-Certificate -FilePath `"$cerPath`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
)
[System.IO.File]::WriteAllLines((Join-Path $OutputDirectory 'windows-build-info.txt'), [string[]] $infoLines, (New-Object System.Text.UTF8Encoding($false)))

if (-not $KeepIntermediate) {
    foreach ($directory in @($stagingRoot, $packagesDirectory, $logsDirectory)) { Remove-Item -LiteralPath $directory -Recurse -Force }
}

Write-Host ''
Write-Host "Bundle: $bundlePath"
Write-Host ("Size:   {0:N1} MB" -f ($bundleFile.Length / 1MB))
Write-Host "SHA256: $bundleHash"
Write-Host "Cert:   $cerPath"
foreach ($installerPath in $installerPaths) { Write-Host "Installer: $installerPath" }
Write-Host "Info:   $(Join-Path $OutputDirectory 'windows-build-info.txt')"

# Keep the console open when the script was started by a double-click.
if (Test-LaunchedFromFileManager) {
    $null = Read-Host 'Press Enter to close this window'
}
