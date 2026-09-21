$ErrorActionPreference = "Stop"

$codings = Split-Path -Parent $MyInvocation.MyCommand.Path
$packages = Join-Path (Split-Path -Parent $codings) "Packages"
$iexpress = Join-Path $env:WINDIR "System32\iexpress.exe"
$sedTemplate = Join-Path $codings "package.sed"
$sed = Join-Path $codings "package.generated.sed"
$output = Join-Path $packages "CursorQuotaPet-Fix-Setup.exe"

if (-not (Test-Path -LiteralPath $iexpress)) {
    throw "找不到 Windows IExpress：$iexpress"
}
New-Item -ItemType Directory -Force -Path $packages | Out-Null
foreach ($required in @("CursorQuotaPet.exe", "CursorQuotaPet.core.exe", "CursorQuotaPet.ico", "install.ps1")) {
    if (-not (Test-Path -LiteralPath (Join-Path $codings $required))) {
        throw "缺少打包文件：$required"
    }
}

$template = Get-Content -LiteralPath $sedTemplate -Raw
$generated = $template.Replace("__OUTPUT_EXE__", $output).Replace("__SOURCE_DIR__", $codings)
Set-Content -LiteralPath $sed -Value $generated -Encoding ASCII

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Force
}

$iexpressProcess = Start-Process -FilePath $iexpress -ArgumentList @('/N', $sed) -PassThru -Wait
$iexpressExitCode = $iexpressProcess.ExitCode
$deadline = (Get-Date).AddSeconds(30)
while (-not (Test-Path -LiteralPath $output) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
}
if (Test-Path -LiteralPath $sed) {
    Remove-Item -LiteralPath $sed -Force
}
if (-not (Test-Path -LiteralPath $output)) {
    throw "IExpress 打包失败，未生成安装包：$output (exit $iexpressExitCode)"
}
if ($null -ne $iexpressExitCode -and $iexpressExitCode -ne 0) {
    throw "IExpress 打包失败：exit $iexpressExitCode"
}

$ddf = Join-Path $codings "~CursorQuotaPet-Fix-Setup.DDF"
if (Test-Path -LiteralPath $ddf) {
    Remove-Item -LiteralPath $ddf -Force
}

Write-Output "已生成：$output"
