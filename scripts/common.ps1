#Requires -Version 5.1
<#
.SYNOPSIS
Shared helpers for the Errandesk build scripts.

.DESCRIPTION
Dot-source this file from a script in the same folder. It sets these script variables:

ScriptDirectory        Folder that contains the build scripts.
LocalRoot              scripts\local inside the app repository, or the build repository root
                       when the scripts live in a separate build repository.
DefaultRepositoryRoot  The app repository that contains the scripts folder, or the Errandesk
                       folder next to a separate build repository.
#>

$script:ScriptDirectory = $PSScriptRoot
$script:BuildRoot = Split-Path -Parent $PSScriptRoot

if (Test-Path -LiteralPath (Join-Path $script:BuildRoot 'src\Errandesk.Client\Errandesk.Client.csproj') -PathType Leaf) {
    $script:LocalRoot = Join-Path $script:ScriptDirectory 'local'
    $script:DefaultRepositoryRoot = $script:BuildRoot
}
else {
    $script:LocalRoot = $script:BuildRoot
    $script:DefaultRepositoryRoot = Join-Path (Split-Path -Parent $script:BuildRoot) 'Errandesk'
}

function Test-LaunchedFromFileManager {
    try {
        $current = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $PID" -ErrorAction Stop
        for ($level = 0; $level -lt 2; $level++) {
            if (-not $current -or -not $current.ParentProcessId) { return $false }
            $parent = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $($current.ParentProcessId)" -ErrorAction Stop
            if (-not $parent) { return $false }
            if ($parent.Name -eq 'explorer.exe') { return $true }
            # Windows reports both the Files and Files - Preview apps as Files.exe, so check the package path.
            if ($parent.Name -eq 'Files.exe' -and $parent.ExecutablePath -match '\\49306atecsolution\.Files(UWP|Preview)_') { return $true }
            # A double-clicked .cmd starts cmd.exe, so its parent is the file manager.
            if ($parent.Name -ne 'cmd.exe') { return $false }
            $current = $parent
        }
        return $false
    }
    catch {
        return $false
    }
}

function Read-SettingsFile {
    param([string] $Path)

    $settings = @{}
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $settings }

    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) { continue }

        $separator = $trimmed.IndexOf('=')
        if ($separator -lt 1) { throw "The settings file line is not NAME=VALUE: $line" }

        $name = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        if ($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $settings[$name] = $value
    }
    return $settings
}

function Resolve-Setting {
    param(
        [hashtable] $Settings,
        [string[]] $Names,
        [string] $Default
    )

    foreach ($name in $Names) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    }
    foreach ($name in $Names) {
        if ($Settings.ContainsKey($name) -and -not [string]::IsNullOrWhiteSpace($Settings[$name])) { return $Settings[$name] }
    }
    return $Default
}

function Resolve-InputPath {
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $Path }
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return Join-Path $script:LocalRoot $Path
}

function Get-SettingsPath {
    param([string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetFullPath($script:LocalRoot)
    $prefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { return $fullPath.Substring($prefix.Length) -replace '\\', '/' }
    return $Path -replace '\\', '/'
}

function Read-PlainTextPassword {
    param([string] $Prompt)

    $securePassword = Read-Host -Prompt $Prompt -AsSecureString
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer) }
}
