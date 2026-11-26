# Errandesk 빌드 스크립트

서명된 Errandesk 클라이언트 패키지를 Android와 Windows용으로 빌드하는 PowerShell 스크립트입니다. macOS, Linux, iOS 자동화는 아직 제공하지 않습니다.

이 저장소의 클라이언트 프로젝트 전용이며, 범용 빌드 도구가 아닙니다.

## 요구 사항

- Windows 10 버전 2004(빌드 19041) 이상
- PowerShell 5.1 이상
- .NET 11 SDK (`global.json`에 지정된 버전)
- Android 빌드용 Android 워크로드, Android SDK, JDK 17 이상
- Windows 빌드용 `makeappx.exe`와 `signtool.exe`가 포함된 Windows SDK
- Windows EXE 설치 프로그램용 `aticmsixgen`
- `scripts` 폴더가 포함된 Errandesk 체크아웃(또는 `-RepositoryRoot` 지정)

첫 빌드 전에 환경 검사를 실행합니다.

```powershell
.\setup-environment.ps1 -Check
```

## 환경 설정

`setup-environment.ps1`은 환경을 점검하고 필요에 따라 빠진 도구를 설치합니다.

```powershell
# 현재 상태 확인
.\setup-environment.ps1 -Check

# 누락된 도구 설치
.\setup-environment.ps1 -Install
```

| 매개 변수 | 용도 |
|---|---|
| `-Install` | `winget`과 `dotnet` CLI로 누락된 도구를 설치합니다. |
| `-Check` | 환경을 변경하지 않고 상태만 확인합니다. 기본 동작입니다. |
| `-IncludeIos` | 솔루션 빌드용 `ios` 워크로드도 확인하거나 설치합니다. |
| `-SkipAndroid` | Android 워크로드, SDK, JDK, `adb` 검사를 건너뜁니다. |
| `-SkipWindows` | Windows SDK와 `aticmsixgen` 검사를 건너뜁니다. |
| `-AndroidSdkDirectory` | 확인하거나 설치할 Android SDK 폴더입니다. |
| `-JavaSdkDirectory` | `InstallAndroidDependencies`에 전달할 JDK 폴더입니다. |
| `-RepositoryRoot` | Errandesk 저장소 루트입니다. |
| `-Force` | `aticmsixgen`이 이미 설치되어 있으면 업데이트합니다. |

`-Install`을 실행하려면 관리자 권한 터미널과 인터넷 연결이 필요할 수 있습니다. Android SDK를 설치할 때는 라이선스에 동의한 것으로 처리합니다. `ANDROID_HOME`과 `JAVA_HOME` 사용자 환경 변수는 비어 있을 때만 설정합니다. 새로 설치한 도구가 인식되지 않으면 터미널을 다시 열어야 합니다.

주요 설치 단계를 수동으로 진행할 때는 Errandesk 저장소 루트에서 아래 명령을 실행합니다.

```powershell
winget install --id Microsoft.DotNet.SDK.11 --exact
dotnet workload install android
dotnet build src\Errandesk.Client\Errandesk.Client.csproj -f net11.0-android -t:InstallAndroidDependencies -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" -p:AcceptAndroidSDKLicenses=True -p:ErrandeskIncludeIosTarget=false
winget install --id Microsoft.OpenJDK.17 --exact
winget install --id Microsoft.WindowsSDK.10.0.26100 --exact
dotnet tool install --global aticmsixgen
```

이 스크립트는 `-p:ErrandeskIncludeIosTarget=false`를 전달하므로 `ios` 워크로드가 필요 없습니다.

## 저장소 배치

스크립트는 `scripts` 폴더 위치를 기준으로 저장소 구성을 판별합니다.

### 클라이언트 저장소 내부

```text
Errandesk/
├─ scripts/
│  ├─ build-android.ps1
│  ├─ ...
│  └─ local/               로컬 설정과 빌드 출력
├─ src/
└─ README.md
```

이 구성에서는 `scripts`의 상위 폴더가 앱 저장소입니다. 로컬 파일과 빌드 출력은 `scripts/local`에 저장되며 커밋하지 않습니다.

### 별도 빌드 저장소

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

이 구성에서는 앱 저장소와 빌드 저장소가 같은 상위 폴더 아래 나란히 위치하며, 로컬 파일과 빌드 출력은 빌드 저장소 루트에 저장됩니다. 두 저장소가 나란히 있지 않으면 `-RepositoryRoot`를 전달해야 합니다.

`-SettingsFile`과 `-OutputDirectory`로 기본 경로를 바꿀 수 있습니다. 상대 경로는 로컬 루트를 기준으로 해석합니다.

## 로컬 파일

로컬 루트는 앱 저장소 내부 구성에서는 `scripts\local`, 별도 빌드 저장소 구성에서는 빌드 저장소 루트입니다.

예제 파일을 로컬 루트에 복사한 뒤 경로를 입력합니다.

```powershell
# 앱 저장소 내부
New-Item -ItemType Directory -Force -Path .\scripts\local
Copy-Item .\scripts\errandesk-release.env.example .\scripts\local\errandesk-release.env
Copy-Item .\scripts\windows-signing.env.example .\scripts\local\windows-signing.env

# 별도 빌드 저장소에서는 빌드 저장소 루트에서 실행
Copy-Item .\scripts\errandesk-release.env.example .\errandesk-release.env
Copy-Item .\scripts\windows-signing.env.example .\windows-signing.env
```

다음 파일은 커밋하면 안 됩니다.

- `errandesk-release.env`와 `windows-signing.env`
- Android keystore (`ks`, `.jks`, `.keystore` 파일)
- Windows `.pfx`
- `google-services.json`
- `secrets` 아래의 비밀번호 파일

`.example` 파일에는 자리 표시자만 들어 있습니다.

## Android

### 서명 자료 생성

`setup-signing.ps1`은 keystore, 비밀번호 파일 두 개, `errandesk-release.env`가 없으면 새로 만듭니다.

```powershell
.\setup-signing.ps1
```

실행하면 keystore 비밀번호와 키 별칭 비밀번호를 따로 묻고, 각각 `secrets/android-keystore-password.txt`와 `secrets/android-key-password.txt`에 저장합니다. 환경 파일에는 두 비밀번호 파일의 경로만 기록합니다. 두 비밀번호가 다르면 keystore에 별도 비밀번호를 쓸 수 있도록 JKS keystore를 만듭니다. PKCS12 keystore는 keystore와 키에 같은 비밀번호를 사용해야 합니다.

| 매개 변수 | 용도 |
|---|---|
| `-Alias` | 키 별칭입니다. 기본값은 `errandesk`입니다. |
| `-Keystore` | keystore 파일입니다. 기본값은 로컬 루트의 `ks`입니다. |
| `-GoogleServicesJson` | `google-services.json` 경로입니다. |
| `-PasswordFile` | 비밀번호 하나를 파일에서 읽어 keystore와 키에 모두 사용합니다. |
| `-StorePasswordFile` | keystore 비밀번호를 파일에서 읽습니다. |
| `-KeyPasswordFile` | 키 별칭 비밀번호를 파일에서 읽습니다. |
| `-Force` | 기존 서명 파일을 백업으로 옮기고 새로 만듭니다. |

`-Force`를 쓰면 새 서명 ID가 생성되므로, 이전 키로 설치한 앱을 제자리에서 업데이트할 수 없습니다.

### APK 빌드

```powershell
.\build-android.ps1
```

`keytool`로 keystore 비밀번호를 검증하고 `google-services.json`에 애플리케이션 ID가 등록되어 있는지 확인한 뒤, `net11.0-android`를 Release 구성으로 게시해 서명된 APK와 `build-info.txt`를 출력 폴더로 복사합니다. 서명할 때는 keystore 비밀번호와 별도로 키 별칭 비밀번호를 사용합니다.

### 설정

설정 파일은 `NAME=VALUE` 형식을 따르며, 매개 변수와 환경 변수가 설정 파일보다 우선합니다.

| 변수 | 용도 |
|---|---|
| `ERRANDESK_ANDROID_KEYSTORE` | keystore 파일입니다. |
| `ERRANDESK_ANDROID_KEY_ALIAS` | 키 별칭입니다. |
| `ERRANDESK_ANDROID_KEYSTORE_PASSWORD` | 평문 keystore 비밀번호입니다. |
| `ERRANDESK_ANDROID_KEYSTORE_PASSWORD_FILE` | keystore 비밀번호 파일입니다. |
| `ERRANDESK_ANDROID_KEY_PASSWORD` | 평문 키 별칭 비밀번호입니다. |
| `ERRANDESK_ANDROID_KEY_PASSWORD_FILE` | 키 별칭 비밀번호 파일입니다. |
| `ERRANDESK_ANDROID_GOOGLE_SERVICES_JSON` | `google-services.json` 파일입니다. |
| `ERRANDESK_ANDROID_OUTPUT_DIRECTORY` | 출력 폴더입니다. |
| `ERRANDESK_ANDROID_VERSION` | 표시 버전입니다. |
| `ERRANDESK_ANDROID_VERSION_CODE` | Android 버전 코드입니다. |
| `ERRANDESK_ANDROID_SETTINGS_FILE` | 환경 파일 경로입니다. |

`ERRANDESK_ANDROID_KEYSTORE_PASSWORD`와 `ERRANDESK_ANDROID_KEYSTORE_PASSWORD_FILE`은 선택 사항입니다. 둘 다 없으면 키 별칭 비밀번호를 keystore에도 사용합니다. 비밀번호를 분리하려면 JKS keystore가 필요하며, PKCS12 keystore는 keystore와 키에 같은 비밀번호를 사용해야 합니다. `ERRANDESK_ANDROID_STORE_PASSWORD`와 `ERRANDESK_ANDROID_STORE_PASSWORD_FILE`은 keystore 비밀번호 이름의 별칭으로 사용할 수 있습니다.

프로젝트에서 쓰는 `ERRANDESK_CLIENT_*` 변수(`ERRANDESK_CLIENT_ANDROID_KEYSTORE`, `ERRANDESK_CLIENT_ANDROID_KEY_ALIAS`, `ERRANDESK_CLIENT_ANDROID_KEYSTORE_PASSWORD_FILE`, `ERRANDESK_CLIENT_ANDROID_KEY_PASSWORD_FILE`, `ERRANDESK_CLIENT_GOOGLE_SERVICES_JSON`)도 함께 읽습니다.

### 매개 변수

| 매개 변수 | 용도 |
|---|---|
| `-RepositoryRoot` | Errandesk 저장소 루트입니다. |
| `-Configuration` | 빌드 구성입니다. 기본값은 `Release`입니다. |
| `-Version` | 표시 버전입니다. 기본값은 `Directory.Build.props`입니다. |
| `-VersionCode` | Android 버전 코드입니다. 기본값은 `major * 10000 + minor * 100 + patch`입니다. |
| `-OutputDirectory` | 출력 폴더입니다. 기본값은 `ERRANDESK_ANDROID_OUTPUT_DIRECTORY`이고, 이 값이 없으면 로컬 루트입니다. |
| `-SettingsFile` | 설정 파일입니다. |
| `-Keystore` | keystore 파일입니다. |
| `-KeyAlias` | 키 별칭입니다. |
| `-GoogleServicesJson` | `google-services.json` 파일입니다. |
| `-ApplicationId` | Android 패키지 이름입니다. 기본값은 `com.airtaxi.errandesk`입니다. |
| `-Bundle` | AAB도 함께 생성합니다. |
| `-Install` | `adb install -r`로 연결된 기기에 APK를 설치합니다. |
| `-SkipFirebase` | `google-services.json`과 FCM 없이 빌드합니다. |

자동으로 계산한 버전 코드는 `minor`와 `patch`가 100 미만일 때만 고유합니다. 예를 들어 `1.0.100`과 `1.1.0`은 모두 `10100`이 됩니다. 버전 구성 요소가 100 이상이 되면 `-VersionCode`를 전달하거나 `ERRANDESK_ANDROID_VERSION_CODE`를 설정해야 합니다.

### 예시

```powershell
# 릴리스 APK
.\build-android.ps1

# 버전 1.0.0, AAB, 기기 설치
.\build-android.ps1 -Version 1.0.0 -Bundle -Install

# Firebase 없이 빌드
.\build-android.ps1 -SkipFirebase
```

### 출력

- `Errandesk-<version>.apk`와 `Errandesk-latest.apk`
- `-Bundle` 사용 시 `Errandesk-<version>.aab`
- `build-info.txt`

APK가 두 개 이상 생성되면 각 파일 이름에 ABI가 붙고 `Errandesk-latest.apk`는 만들어지지 않습니다. 이때는 `stage-client-update.ps1`에 `-AndroidApk`를 전달합니다.

## Windows

```powershell
.\build-windows-msix.ps1
```

`Directory.Build.props`에서 버전을 읽어 아키텍처별로 `net11.0-windows10.0.26100.0`을 빌드하고, MSIX를 패키징해 각 패키지와 번들에 서명합니다. 번들 검증이 끝나면 `aticmsixgen`으로 EXE 설치 프로그램을 생성합니다.

첫 실행 시 주체가 `O=Howon Lee`인 자체 서명 코드 서명 인증서를 생성합니다. 이 주체는 `build-windows-msix.ps1` 상단의 `$script:WindowsPublisher` 한 곳에서만 지정하며, 클라이언트 프로젝트의 `ApplicationPublisher`와 일치해야 합니다. PFX와 CER을 내보낸 뒤에는 인증서와 개인 키를 `CurrentUser\My` 저장소에서 제거하므로, 키는 PFX에만 남습니다.

### 설정

| 변수 | 용도 |
|---|---|
| `ERRANDESK_WINDOWS_PFX` | PFX 파일입니다. |
| `ERRANDESK_WINDOWS_PFX_PASSWORD` | 평문 PFX 비밀번호입니다. |
| `ERRANDESK_WINDOWS_PFX_PASSWORD_FILE` | PFX 비밀번호 파일입니다. |
| `ERRANDESK_WINDOWS_CER` | CER 파일입니다. |

비밀번호가 설정되지 않은 상태에서 인증서를 새로 만들면 비밀번호를 생성해 지정된 비밀번호 파일에 저장합니다. 비밀번호 파일이 지정되지 않았다면 `secrets/windows-pfx-password.txt`에 저장합니다. `windows-signing.env`는 설정 파일이 아직 없을 때만 파일 경로를 담아 새로 만듭니다. 기존 설정 파일은 다시 쓰지 않으며, 비밀번호 파일 경로가 빠져 있다면 추가해야 할 줄을 출력합니다.

### 매개 변수

| 매개 변수 | 용도 |
|---|---|
| `-RepositoryRoot` | Errandesk 저장소 루트입니다. |
| `-Configuration` | 빌드 구성입니다. 기본값은 `Release`입니다. |
| `-Architectures` | 빌드할 아키텍처입니다(예: `arm64,x64`). 기본값은 `arm64`와 `x64`입니다. |
| `-Revision` | MSIX 버전의 네 번째 구성 요소입니다. |
| `-OutputDirectory` | 출력 폴더입니다. |
| `-SettingsFile` | 설정 파일입니다. |
| `-KeepIntermediate` | 스테이징, 아키텍처별 패키지, 로그 폴더를 유지합니다. |
| `-SkipInstallers` | MSIX 번들만 생성합니다. |

### 인증서 신뢰

생성된 인증서는 자체 서명이므로, 패키지를 설치하기 전에 대상 PC에 CER을 설치해야 합니다.

```powershell
Import-Certificate -FilePath .\scripts\local\Errandesk-Windows.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

관리자 권한 PowerShell에서 실행해야 합니다. 별도 빌드 저장소 구성이라면 경로를 그에 맞게 바꿉니다.

### 출력

- `Errandesk-<msixversion>.msixbundle`
- `Installer-arm64.exe`와 `Installer-x64.exe`
- `windows-build-info.txt`

### 예시

```powershell
.\build-windows-msix.ps1
.\build-windows-msix.ps1 -Architectures x64 -SkipInstallers
.\build-windows-msix.ps1 -Revision 2
```

## 전체 빌드

`build-all.ps1`은 Android 빌드에 이어 Windows 빌드를 순서대로 실행하며, 중간에 실패하면 중단합니다.

```powershell
.\build-all.ps1
```

| 매개 변수 | 용도 |
|---|---|
| `-RepositoryRoot` | Errandesk 저장소 루트입니다. |
| `-Configuration` | 빌드 구성입니다. 기본값은 `Release`입니다. |
| `-OutputDirectory` | 두 빌드가 함께 사용할 출력 폴더입니다. 지정하지 않으면 Android 빌드는 `ERRANDESK_ANDROID_OUTPUT_DIRECTORY`를 따르고, 각 빌드는 로컬 루트를 기본값으로 사용합니다. |
| `-SkipAndroid` | Android 빌드를 건너뜁니다. |
| `-SkipWindows` | Windows 빌드를 건너뜁니다. |
| `-AndroidSettingsFile` | Android 설정 파일입니다. |
| `-WindowsSettingsFile` | Windows 설정 파일입니다. |
| `-Version` | Android 표시 버전입니다. |
| `-VersionCode` | Android 버전 코드입니다. |
| `-Revision` | MSIX 네 번째 버전 구성 요소입니다. |
| `-Bundle` | AAB도 함께 생성합니다. |
| `-Install` | 연결된 기기에 APK를 설치합니다. |
| `-SkipFirebase` | Firebase 없이 Android 클라이언트를 빌드합니다. |
| `-SkipInstallers` | Windows EXE 설치 프로그램을 건너뜁니다. |

```powershell
.\build-all.ps1
.\build-all.ps1 -SkipWindows
.\build-all.ps1 -SkipAndroid -SkipInstallers
```

## 클라이언트 업데이트 스테이징

`stage-client-update.ps1`은 `ERRANDESK_CLIENT_UPDATE_BASE_URL`에서 제공할 폴더 구조를 만듭니다.

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

| 매개 변수 | 용도 |
|---|---|
| `-Destination` | 출력 폴더입니다. 기본값은 로컬 루트의 `client-updates`입니다. |
| `-AndroidVersion` | Android 버전입니다. 기본값은 `build-info.txt`입니다. |
| `-WindowsVersion` | Windows 버전입니다. 기본값은 `windows-build-info.txt`입니다. |
| `-AndroidApk` | 스테이징할 APK입니다. |
| `-WindowsX64Installer` | x64 설치 프로그램입니다. |
| `-WindowsArm64Installer` | ARM64 설치 프로그램입니다. |
| `-SkipAndroid` | Android 파일을 건너뜁니다. |
| `-SkipWindows` | Windows 파일을 건너뜁니다. |

스크립트는 설치 파일을 먼저 복사한 뒤 버전 파일을 마지막에 기록합니다. 서버에 올릴 때도 같은 순서로 설치 파일을 먼저 업로드하고 각 `version` 파일을 마지막에 교체해야 합니다.

Windows 버전은 MSIX 패키지 버전이며, 표시 버전 뒤에 `ApplicationVersion`을 네 번째 구성 요소로 붙인 값입니다. Android 버전은 표시 버전입니다.

## 문제 해결

| 증상 | 해결 |
|---|---|
| `keytool`을 찾을 수 없음 | `.\setup-environment.ps1 -Install`을 실행하거나 JDK 17 이상을 설치합니다. |
| `google-services.json registers [...] not com.airtaxi.errandesk` | Firebase 프로젝트에 패키지를 등록하거나 `-ApplicationId`를 전달하거나 `-SkipFirebase`를 사용합니다. |
| `aticmsixgen`을 찾을 수 없음 | `dotnet tool install --global aticmsixgen`을 실행하거나 `-SkipInstallers`를 전달합니다. |
| `makeappx.exe` 또는 `signtool.exe`를 찾을 수 없음 | Windows SDK를 설치하거나 `.\setup-environment.ps1 -Install`을 실행합니다. |
| MSIX는 설치되지만 인증서를 신뢰할 수 없음 | CER을 Local Machine > Trusted People에 가져옵니다. |
| `No signed APK was produced` | keystore, 별칭, 두 비밀번호를 확인합니다. |
| keystore를 열 수 없음 | `ERRANDESK_ANDROID_KEYSTORE_PASSWORD` 또는 `ERRANDESK_ANDROID_KEYSTORE_PASSWORD_FILE`을 확인합니다. |
| 키 비밀번호가 맞지 않아 서명 실패 | `ERRANDESK_ANDROID_KEY_PASSWORD` 또는 `ERRANDESK_ANDROID_KEY_PASSWORD_FILE`을 확인합니다. |
| `adb`를 찾을 수 없음 | Android SDK platform-tools를 설치하거나 `-Install`을 생략합니다. |
| Errandesk 저장소를 찾을 수 없음 | `-RepositoryRoot`에 저장소 경로를 전달합니다. |
| PowerShell이 `.ps1` 파일을 차단함 | `-ExecutionPolicy Bypass`를 전달하는 `.cmd` 래퍼를 사용합니다. |

## 보안

- `errandesk-release.env`, `windows-signing.env`, keystore, PFX 파일, `google-services.json`, 비밀번호 파일, 서비스 계정 키를 버전 관리에 포함하면 안 됩니다.
- Android keystore와 비밀번호는 반드시 백업해 두어야 합니다. 분실하면 이미 설치된 앱을 업데이트할 수 없습니다.
- 자동 생성되는 Windows 인증서는 로컬 배포용 자체 서명 인증서입니다. 더 넓게 배포하려면 신뢰할 수 있는 코드 서명 인증서를 사용해야 합니다.
- `keytool`은 Android 비밀번호를 임시 환경 변수에서 읽으므로, 비밀번호가 프로세스 인수에 나타나지 않습니다. `signtool`에는 이런 옵션이 없어서 서명하는 동안 Windows PFX 비밀번호가 프로세스 인수로 전달됩니다. 스크립트는 신뢰할 수 있는 컴퓨터에서 실행해야 합니다.
