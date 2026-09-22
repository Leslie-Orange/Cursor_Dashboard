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
$sourceIcon = Join-Path $payloadRoot "CursorQuotaPet.ico"
foreach ($required in @($sourceExe, $sourceIcon)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Package is missing: $required"
    }
}

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
$targetExe = Join-Path $InstallRoot "CursorQuotaPet.exe"

if (-not $SkipProcessStop) {
    $fullTargetExe = [IO.Path]::GetFullPath($targetExe)
    Get-Process -Name "CursorQuotaPet" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and [IO.Path]::GetFullPath($_.Path) -eq $fullTargetExe) {
                Stop-Process -Id $_.Id -Force
            }
        }
        catch {
        }
    }
    Start-Sleep -Milliseconds 300
}

Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
Copy-Item -LiteralPath $sourceIcon -Destination (Join-Path $InstallRoot "CursorQuotaPet.ico") -Force
foreach ($stale in @("CursorQuotaPet.core.exe", "CursorQuotaPet.original.exe")) {
    $stalePath = Join-Path $InstallRoot $stale
    if (Test-Path -LiteralPath $stalePath) {
        Remove-Item -LiteralPath $stalePath -Force -ErrorAction SilentlyContinue
    }
}

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
    $message = $appTitle + [string]::Concat(
        [char]0x5DF2, [char]0x5B89, [char]0x88C5, "。",
        [char]0x8BF7, [char]0x5728, [char]0x4EFB, [char]0x52A1, [char]0x680F, [char]0x901A, [char]0x77E5, [char]0x533A, [char]0x57DF, [char]0x67E5, [char]0x770B, "。",
        [char]0x9F20, [char]0x6807, [char]0x79FB, [char]0x5230, [char]0x56FE, [char]0x6807, [char]0x4E0A, [char]0xFF0C,
        [char]0x4F59, [char]0x989D, [char]0x4F1A, [char]0x663E, [char]0x793A, [char]0x5728, [char]0x56FE, [char]0x6807, [char]0x6B63, [char]0x4E0A, [char]0x65B9, "。"
    )
    [Windows.Forms.MessageBox]::Show(
        $message,
        $appTitle,
        [Windows.Forms.MessageBoxButtons]::OK,
        [Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
