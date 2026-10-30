$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF"
$output = Join-Path $root "CursorQuotaPet.exe"
$icon = Join-Path $root "CursorQuotaPet.ico"
$manifest = Join-Path $root "app.manifest"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "找不到 .NET Framework C# 编译器：$compiler"
}
if (-not (Test-Path -LiteralPath $manifest)) {
    throw "缺少文件：$manifest"
}

$iconCompiler = Join-Path $root "MakeAppIcon.cs"
$iconShared = Join-Path $root "AppIcon.cs"
$iconTool = Join-Path $root "MakeAppIcon.exe"
& $compiler /nologo /target:exe /optimize+ /codepage:65001 `
    /out:$iconTool `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    $iconCompiler `
    $iconShared
if ($LASTEXITCODE -ne 0) {
    throw "图标工具编译失败：exit $LASTEXITCODE"
}
& $iconTool $icon
if ($LASTEXITCODE -ne 0) {
    throw "应用图标生成失败：exit $LASTEXITCODE"
}
Remove-Item -LiteralPath $iconTool -Force -ErrorAction SilentlyContinue

$sources = @(
    "Program.cs",
    "QuotaData.cs",
    "QuotaUi.cs",
    "AppIcon.cs"
) | ForEach-Object { Join-Path $root $_ }

& $compiler /nologo /target:winexe /optimize+ /codepage:65001 `
    /out:$output `
    /win32icon:$icon `
    /win32manifest:$manifest `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    /reference:"$wpf\WindowsBase.dll" `
    /reference:"$wpf\UIAutomationTypes.dll" `
    /reference:"$wpf\UIAutomationClient.dll" `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "编译失败：exit $LASTEXITCODE"
}

Write-Output "已生成：$output"
