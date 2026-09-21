$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$iexpress = Join-Path $env:WINDIR "System32\iexpress.exe"
$sed = Join-Path $root "package.sed"
$output = Join-Path $root "CursorQuotaPet-Fix-Setup.exe"

if (-not (Test-Path -LiteralPath $iexpress)) {
    throw "找不到 Windows IExpress：$iexpress"
}
foreach ($required in @("CursorQuotaPet.exe", "CursorQuotaPet.core.exe", "CursorQuotaPet.ico", "install.ps1")) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $required))) {
        throw "缺少打包文件：$required"
    }
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Force
}

$iexpressProcess = Start-Process -FilePath $iexpress -ArgumentList @('/N', $sed) -PassThru -Wait
$iexpressExitCode = $iexpressProcess.ExitCode
$deadline = (Get-Date).AddSeconds(30)
while (-not (Test-Path -LiteralPath $output) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
}
if (-not (Test-Path -LiteralPath $output)) {
    throw "IExpress 打包失败，未生成安装包：$output (exit $iexpressExitCode)"
}
if ($null -ne $iexpressExitCode -and $iexpressExitCode -ne 0) {
    throw "IExpress 打包失败：exit $iexpressExitCode"
}

$ddf = Join-Path $root "~CursorQuotaPet-Fix-Setup.DDF"
if (Test-Path -LiteralPath $ddf) {
    Remove-Item -LiteralPath $ddf -Force
}

Write-Output "已生成：$output"
