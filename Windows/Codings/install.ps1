param(
    [string]$InstallRoot,
    [switch]$NoShortcuts,
    [switch]$NoMessage,
    [switch]$SkipProcessStop
)

$ErrorActionPreference = "Stop"
$codings = Split-Path -Parent $MyInvocation.MyCommand.Path
$setupPath = Join-Path (Split-Path -Parent $codings) "Packages\CursorQuotaPet-Setup.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "找不到安装包，请先运行 build-package.ps1：$setupPath"
}

# Use the same installer so script installs also receive the system uninstall entry.
$setupArguments = @("--quiet")
if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
    $resolvedRoot = [IO.Path]::GetFullPath($InstallRoot)
    $setupArguments += @("--root", ('"' + $resolvedRoot + '"'))
}
if ($NoShortcuts) { $setupArguments += "--no-shortcuts" }
if ($SkipProcessStop) { $setupArguments += "--skip-process-stop" }
$setup = Start-Process -FilePath $setupPath -ArgumentList $setupArguments -WindowStyle Hidden -Wait -PassThru
if ($setup.ExitCode -ne 0) {
    throw "安装失败：exit $($setup.ExitCode)"
}

if (-not $NoMessage) {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show(
        'Cursor仪表盘已安装。请在任务栏通知区域查看；可在 Windows“已安装的应用”中卸载。',
        "Cursor仪表盘",
        [Windows.Forms.MessageBoxButtons]::OK,
        [Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}