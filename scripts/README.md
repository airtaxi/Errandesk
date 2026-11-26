# Errandesk build scripts

PowerShell scripts that build signed Errandesk client packages for Android and Windows. macOS, Linux, and iOS automation is not available yet.

The scripts target this repository's client project. They are not general-purpose build tools.

## Requirements

- Windows 10 version 2004 (build 19041) or later
- PowerShell 5.1 or later
- .NET 11 SDK (the version pinned by `global.json`)
- Android workload, Android SDK, and JDK 17 or later for Android builds
- Windows SDK with `makeappx.exe` and `signtool.exe` for Windows builds
- `aticmsixgen` for the Windows EXE installers
- An Errandesk checkout that contains the `scripts` folder, or `-RepositoryRoot`

Run the environment check before the first build:

```powershell
.\setup-environment.ps1 -Check
```

## Environment setup

`setup-environment.ps1` checks the machine and optionally installs the missing tools.

```powershell
# Report the current state
.\setup-environment.ps1 -Check

# Install missing tools
.\setup-environment.ps1 -Install
```

| Parameter | Purpose |
|---|---|
| `-Install` | Install missing tools with `winget` and the `dotnet` CLI. |
| `-Check` | Report status without changing the machine. This is the default. |
| `-IncludeIos` | Also check or install the `ios` workload for solution builds. |
| `-SkipAndroid` | Skip the Android workload, SDK, JDK, and `adb` checks. |
| `-SkipWindows` | Skip the Windows SDK and `aticmsixgen` checks. |
| `-AndroidSdkDirectory` | Android SDK folder to install or check. |
| `-JavaSdkDirectory` | JDK folder passed to `InstallAndroidDependencies`. |
| `-RepositoryRoot` | Errandesk repository root. |
| `-Force` | Update `aticmsixgen` when it is already installed. |

`-Install` may require an elevated terminal and internet access. It accepts the Android SDK licenses when it installs the Android SDK. It sets the `ANDROID_HOME` and `JAVA_HOME` user environment variables only when they are empty. If a newly installed tool is not found, reopen the terminal.

To run the main install steps manually, execute these commands from the Errandesk repository root:

```powershell
winget install --id Microsoft.DotNet.SDK.11 --exact
dotnet workload install android
dotnet build src\Errandesk.Client\Errandesk.Client.csproj -f net11.0-android -t:InstallAndroidDependencies -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" -p:AcceptAndroidSDKLicenses=True -p:ErrandeskIncludeIosTarget=false
winget install --id Microsoft.OpenJDK.17 --exact
winget install --id Microsoft.WindowsSDK.10.0.26100 --exact
dotnet tool install --global aticmsixgen
```

These scripts do not require the iOS workload because they pass `-p:ErrandeskIncludeIosTarget=false`.

## Repository layouts

The scripts detect their layout from the location of the `scripts` folder.

### Inside the client repository

```text
Errandesk/
├─ scripts/
│  ├─ build-android.ps1
│  ├─ ...
│  └─ local/               local settings and build outputs
├─ src/
└─ README.md
```

In this layout, the app repository is the parent of `scripts`. Local files and build outputs go to `scripts/local`, which is not committed.

### In a separate build repository

```text
Repos/
├─ Errandesk/
│  ├─ src/
│  └─ README.md
└─ Errandesk.Release/
   ├─ scripts/
   ├─ errandesk-release.env
   ├─ windows-signing.env
   ├─ ks
   ├─ google-services.json
   └─ Errandesk-Windows.pfx
```

In this layout, the app repository is a sibling of the build repository, and local files and build outputs stay in the build repository root. Pass `-RepositoryRoot` when the repositories are not siblings.

`-SettingsFile` and `-OutputDirectory` override the default paths. Relative paths resolve from the local root.

## Local files

The local root is `scripts\local` inside the app repository, or the build repository root in the separate layout.

Copy the example files to the local root and fill in the paths:

```powershell
# Inside the app repository
New-Item -ItemType Directory -Force -Path .\scripts\local
Copy-Item .\scripts\errandesk-release.env.example .\scripts\local\errandesk-release.env
Copy-Item .\scripts\windows-signing.env.example .\scripts\local\windows-signing.env

# In a separate build repository, run from the build repository root
Copy-Item .\scripts\errandesk-release.env.example .\errandesk-release.env
Copy-Item .\scripts\windows-signing.env.example .\windows-signing.env
```

Never commit:

- `errandesk-release.env` and `windows-signing.env`
- the Android keystore (`ks` or any `.jks` or `.keystore` file)
- the Windows `.pfx`
- `google-services.json`
- password files under `secrets`

The `.example` files contain placeholders only.

## Android

### Create signing material

`setup-signing.ps1` creates a keystore, two password files, and `errandesk-release.env` when they do not exist.

```powershell
.\setup-signing.ps1
```

The script prompts for the keystore password and the key alias password separately, writes them to `secrets/android-keystore-password.txt` and `secrets/android-key-password.txt`, and writes only the password file paths to the environment file. When the passwords differ, the script creates a JKS keystore so the keystore can use separate passwords. PKCS12 keystores require the same password for both.

| Parameter | Purpose |
|---|---|
| `-Alias` | Key alias. Defaults to `errandesk`. |
| `-Keystore` | Keystore file. Defaults to `ks` in the local root. |
| `-GoogleServicesJson` | `google-services.json` path. |
| `-PasswordFile` | Read one password from a file and use it for both. |
| `-StorePasswordFile` | Read the keystore password from a file. |
| `-KeyPasswordFile` | Read the key alias password from a file. |
| `-Force` | Move existing signing files to a backup and create new ones. |

`-Force` creates a new signing identity. Apps installed with the previous key cannot be updated in place.

### Build the APK

```powershell
.\build-android.ps1
```

The script validates the keystore with the keystore password, checks that `google-services.json` registers the application ID, publishes `net11.0-android` in Release configuration, and copies the signed APK and `build-info.txt` to the output folder. Signing uses the key alias password separately from the keystore password.

### Settings

The settings file accepts `NAME=VALUE` lines. Parameters and environment variables take precedence over the settings file.

| Variable | Purpose |
|---|---|
| `ERRANDESK_ANDROID_KEYSTORE` | Keystore file. |
| `ERRANDESK_ANDROID_KEY_ALIAS` | Key alias. |
| `ERRANDESK_ANDROID_KEYSTORE_PASSWORD` | Plain-text keystore password. |
| `ERRANDESK_ANDROID_KEYSTORE_PASSWORD_FILE` | Keystore password file. |
| `ERRANDESK_ANDROID_KEY_PASSWORD` | Plain-text key alias password. |
| `ERRANDESK_ANDROID_KEY_PASSWORD_FILE` | Key alias password file. |
| `ERRANDESK_ANDROID_GOOGLE_SERVICES_JSON` | `google-services.json` file. |
| `ERRANDESK_ANDROID_OUTPUT_DIRECTORY` | Output folder. |
| `ERRANDESK_ANDROID_VERSION` | Display version. |
| `ERRANDESK_ANDROID_VERSION_CODE` | Android version code. |
| `ERRANDESK_ANDROID_SETTINGS_FILE` | Environment file path. |

`ERRANDESK_ANDROID_KEYSTORE_PASSWORD` and `ERRANDESK_ANDROID_KEYSTORE_PASSWORD_FILE` are optional. When neither is set, the key alias password is used for both. Separate passwords require a JKS keystore; PKCS12 keystores use the same password for the keystore and the key. `ERRANDESK_ANDROID_STORE_PASSWORD` and `ERRANDESK_ANDROID_STORE_PASSWORD_FILE` are accepted as aliases for the keystore password names.

The script also reads the `ERRANDESK_CLIENT_*` names used by the project: `ERRANDESK_CLIENT_ANDROID_KEYSTORE`, `ERRANDESK_CLIENT_ANDROID_KEY_ALIAS`, `ERRANDESK_CLIENT_ANDROID_KEYSTORE_PASSWORD_FILE`, `ERRANDESK_CLIENT_ANDROID_KEY_PASSWORD_FILE`, and `ERRANDESK_CLIENT_GOOGLE_SERVICES_JSON`.

### Parameters

| Parameter | Purpose |
|---|---|
| `-RepositoryRoot` | Errandesk repository root. |
| `-Configuration` | Build configuration. Defaults to `Release`. |
| `-Version` | Display version. Defaults to `Directory.Build.props`. |
| `-VersionCode` | Android version code. Defaults to `major * 10000 + minor * 100 + patch`. |
| `-OutputDirectory` | Output folder. Defaults to `ERRANDESK_ANDROID_OUTPUT_DIRECTORY`, then the local root. |
| `-SettingsFile` | Settings file. |
| `-Keystore` | Keystore file. |
| `-KeyAlias` | Key alias. |
| `-GoogleServicesJson` | `google-services.json` file. |
| `-ApplicationId` | Android package name. Defaults to `com.airtaxi.errandesk`. |
| `-Bundle` | Also produce an AAB. |
| `-Install` | Install the APK on the connected device with `adb install -r`. |
| `-SkipFirebase` | Build without `google-services.json` and FCM. |

The calculated version code is unique only while `minor` and `patch` stay below 100. For example, `1.0.100` and `1.1.0` both produce `10100`. Pass `-VersionCode` or set `ERRANDESK_ANDROID_VERSION_CODE` when a version part reaches 100.

### Examples

```powershell
# Release APK
.\build-android.ps1

# Version 1.0.0, AAB, and install on the device
.\build-android.ps1 -Version 1.0.0 -Bundle -Install

# Build without Firebase
.\build-android.ps1 -SkipFirebase
```

### Outputs

- `Errandesk-<version>.apk` and `Errandesk-latest.apk`
- `Errandesk-<version>.aab` with `-Bundle`
- `build-info.txt`

When the build produces more than one APK, the script names each file with its ABI and does not create `Errandesk-latest.apk`. Pass `-AndroidApk` to `stage-client-update.ps1` in that case.

## Windows

```powershell
.\build-windows-msix.ps1
```

The script reads the version from `Directory.Build.props`, builds `net11.0-windows10.0.26100.0` for each architecture, packs one MSIX per architecture, signs the packages and the bundle, validates the bundle, and creates the EXE installers with `aticmsixgen`.

The first run creates a self-signed code-signing certificate with the subject `O=Howon Lee`. The subject is set once in `$script:WindowsPublisher` at the top of `build-windows-msix.ps1` and must match the `ApplicationPublisher` in the client project. After the PFX and CER are exported, the certificate and its private key are removed from the `CurrentUser\My` store, so the PFX holds the only copy of the key.

### Settings

| Variable | Purpose |
|---|---|
| `ERRANDESK_WINDOWS_PFX` | PFX file. |
| `ERRANDESK_WINDOWS_PFX_PASSWORD` | Plain-text PFX password. |
| `ERRANDESK_WINDOWS_PFX_PASSWORD_FILE` | PFX password file. |
| `ERRANDESK_WINDOWS_CER` | CER file. |

When the script creates the certificate without a configured password, it generates one and writes it to the configured password file, or to `secrets/windows-pfx-password.txt` when no password file is configured. It creates `windows-signing.env` with the file paths only when the settings file does not exist yet. An existing settings file is never rewritten; if it lacks the password file path, the script prints the line to add.

### Parameters

| Parameter | Purpose |
|---|---|
| `-RepositoryRoot` | Errandesk repository root. |
| `-Configuration` | Build configuration. Defaults to `Release`. |
| `-Architectures` | Architectures to build, such as `arm64,x64`. Defaults to `arm64` and `x64`. |
| `-Revision` | Fourth part of the MSIX version. |
| `-OutputDirectory` | Output folder. |
| `-SettingsFile` | Settings file. |
| `-KeepIntermediate` | Keep the staging, per-architecture package, and log folders. |
| `-SkipInstallers` | Produce only the MSIX bundle. |

### Certificate trust

The generated certificate is self-signed. Install the CER on the target PC before installing the package:

```powershell
Import-Certificate -FilePath .\scripts\local\Errandesk-Windows.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Run that command from an elevated PowerShell window. Adjust the path when using a separate build repository.

### Outputs

- `Errandesk-<msixversion>.msixbundle`
- `Installer-arm64.exe` and `Installer-x64.exe`
- `windows-build-info.txt`

### Examples

```powershell
.\build-windows-msix.ps1
.\build-windows-msix.ps1 -Architectures x64 -SkipInstallers
.\build-windows-msix.ps1 -Revision 2
```

## Build all

`build-all.ps1` runs the Android build followed by the Windows build, stopping if either fails.

```powershell
.\build-all.ps1
```

| Parameter | Purpose |
|---|---|
| `-RepositoryRoot` | Errandesk repository root. |
| `-Configuration` | Build configuration. Defaults to `Release`. |
| `-OutputDirectory` | Output folder for both builds. When unset, the Android build uses `ERRANDESK_ANDROID_OUTPUT_DIRECTORY`, and each build falls back to the local root. |
| `-SkipAndroid` | Skip the Android build. |
| `-SkipWindows` | Skip the Windows build. |
| `-AndroidSettingsFile` | Android settings file. |
| `-WindowsSettingsFile` | Windows settings file. |
| `-Version` | Android display version. |
| `-VersionCode` | Android version code. |
| `-Revision` | MSIX fourth version part. |
| `-Bundle` | Also produce an AAB. |
| `-Install` | Install the APK on the connected device. |
| `-SkipFirebase` | Build the Android client without Firebase. |
| `-SkipInstallers` | Skip the Windows EXE installers. |

```powershell
.\build-all.ps1
.\build-all.ps1 -SkipWindows
.\build-all.ps1 -SkipAndroid -SkipInstallers
```

## Stage client updates

`stage-client-update.ps1` creates the layout that `ERRANDESK_CLIENT_UPDATE_BASE_URL` serves:

```text
<destination>/
├─ windows/
│  ├─ version
│  ├─ Installer-x64.exe
│  └─ Installer-arm64.exe
└─ android/
   ├─ version
   └─ Errandesk-latest.apk
```

```powershell
.\stage-client-update.ps1 -Destination D:\www\errandesk\updates
```

| Parameter | Purpose |
|---|---|
| `-Destination` | Output folder. Defaults to `client-updates` in the local root. |
| `-AndroidVersion` | Android version. Defaults to `build-info.txt`. |
| `-WindowsVersion` | Windows version. Defaults to `windows-build-info.txt`. |
| `-AndroidApk` | APK to stage. |
| `-WindowsX64Installer` | x64 installer. |
| `-WindowsArm64Installer` | ARM64 installer. |
| `-SkipAndroid` | Skip the Android files. |
| `-SkipWindows` | Skip the Windows files. |

The script copies the packages first and writes the `version` files last. Follow the same order when deploying: upload the packages before replacing each `version` file.

The Windows version is the MSIX package version, which is the display version followed by `ApplicationVersion` as the fourth part. The Android version is the display version.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `keytool` was not found | Run `.\setup-environment.ps1 -Install`, or install JDK 17 or later. |
| `google-services.json registers [...] not com.airtaxi.errandesk` | Register the package in the Firebase project, pass `-ApplicationId`, or use `-SkipFirebase`. |
| `aticmsixgen` was not found | Run `dotnet tool install --global aticmsixgen`, or pass `-SkipInstallers`. |
| `makeappx.exe` or `signtool.exe` was not found | Install the Windows SDK, or run `.\setup-environment.ps1 -Install`. |
| The MSIX installs but the certificate is not trusted | Import the CER into Local Machine > Trusted People. |
| `No signed APK was produced` | Check the keystore, alias, and both passwords. |
| The keystore cannot be opened | Check `ERRANDESK_ANDROID_KEYSTORE_PASSWORD` or `ERRANDESK_ANDROID_KEYSTORE_PASSWORD_FILE`. |
| Signing fails with an incorrect key password | Check `ERRANDESK_ANDROID_KEY_PASSWORD` or `ERRANDESK_ANDROID_KEY_PASSWORD_FILE`. |
| `adb` was not found | Install Android SDK platform-tools, or omit `-Install`. |
| The script cannot find the Errandesk repository | Pass `-RepositoryRoot` with the repository path. |
| PowerShell blocks the `.ps1` file | Use the `.cmd` wrapper, which passes `-ExecutionPolicy Bypass`. |

## Security

- Keep `errandesk-release.env`, `windows-signing.env`, keystores, PFX files, `google-services.json`, password files, and service account keys out of version control.
- Back up the Android keystore and its password. Losing them prevents updates to installed apps.
- The generated Windows certificate is self-signed and intended for local distribution. Use a trusted code-signing certificate for wider distribution.
- `keytool` reads the Android passwords from temporary environment variables, so they do not appear in its process arguments. `signtool` has no such option, so the Windows PFX password is passed as a process argument while signing. Run the scripts on a trusted machine.
