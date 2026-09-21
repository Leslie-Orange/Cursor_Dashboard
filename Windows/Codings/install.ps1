param(
    [string]$InstallRoot,
    [switch]$NoShortcuts,
    [switch]$NoMessage,
    [switch]$SkipProcessStop
)

$ErrorActionPreference = "Stop"
$payloadRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appName = [string]::Concat([char]0x4EEA, [char]0x8868, [char]0x76D8)
$appTitle = "Cursor" + $appName

function Get-ExistingInstallRoot {
    $uninstallRoots = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    )

    foreach ($uninstallRoot in $uninstallRoots) {
        if (-not (Test-Path -LiteralPath $uninstallRoot)) {
            continue
        }
        foreach ($key in (Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue)) {
            $entry = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if ($entry.DisplayName -eq $appTitle -and $entry.InstallLocation) {
                $candidate = [Environment]::ExpandEnvironmentVariables([string]$entry.InstallLocation)
                if (Test-Path -LiteralPath (Join-Path $candidate "CursorQuotaPet.exe")) {
                    return $candidate
                }
            }
        }
    }

    foreach ($drive in (Get-PSDrive -PSProvider FileSystem)) {
        $candidate = Join-Path $drive.Root $appTitle
        if (Test-Path -LiteralPath (Join-Path $candidate "CursorQuotaPet.exe")) {
            return $candidate
        }
    }

    return $null
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Get-ExistingInstallRoot
}
if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $env:LOCALAPPDATA ("Programs\" + $appTitle)
}
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)

$sourceExe = Join-Path $payloadRoot "CursorQuotaPet.exe"
$sourceCore = Join-Path $payloadRoot "CursorQuotaPet.core.exe"
$sourceIcon = Join-Path $payloadRoot "CursorQuotaPet.ico"
foreach ($required in @($sourceExe, $sourceCore, $sourceIcon)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Package is missing: $required"
    }
}

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
$targetExe = Join-Path $InstallRoot "CursorQuotaPet.exe"
$targetCore = Join-Path $InstallRoot "CursorQuotaPet.core.exe"
$targetBackup = Join-Path $InstallRoot "CursorQuotaPet.original.exe"

if (-not $SkipProcessStop) {
    $fullTargetExe = [IO.Path]::GetFullPath($targetExe)
    Get-Process -Name "CursorQuotaPet" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and [IO.Path]::GetFullPath($_.Path) -eq $fullTargetExe) {
                Stop-Process -Id $_.Id -Force
            }
        }
        catch {
            # A stale or inaccessible process path should not block a fresh install.
        }
    }
    Start-Sleep -Milliseconds 300
}

if ((Test-Path -LiteralPath $targetExe) -and -not (Test-Path -LiteralPath $targetBackup)) {
    Copy-Item -LiteralPath $targetExe -Destination $targetBackup
}
Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
Copy-Item -LiteralPath $sourceCore -Destination $targetCore -Force
Copy-Item -LiteralPath $sourceIcon -Destination (Join-Path $InstallRoot "CursorQuotaPet.ico") -Force

if (-not $NoShortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcutTargets = @(
        (Join-Path ([Environment]::GetFolderPath("Programs")) ($appTitle + ".lnk")),
        (Join-Path ([Environment]::GetFolderPath("Desktop")) ($appTitle + ".lnk"))
    )
    foreach ($shortcutPath in $shortcutTargets) {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $targetExe
        $shortcut.Arguments = "--show"
        $shortcut.WorkingDirectory = $InstallRoot
        $shortcut.IconLocation = "$targetExe,0"
        $shortcut.Save()
    }
}

if (-not $NoMessage) {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show(
        ($appTitle + " hotfix installed.`n`nUsage will now be centered above the tray icon."),
        $appTitle,
        [Windows.Forms.MessageBoxButtons]::OK,
        [Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
