$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$source = Join-Path $root "CursorQuotaPetHotfix.cs"
$output = Join-Path $root "CursorQuotaPet.exe"
$icon = Join-Path $root "CursorQuotaPet.ico"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "找不到 .NET Framework C# 编译器：$compiler"
}
if (-not (Test-Path -LiteralPath (Join-Path $root "CursorQuotaPet.core.exe"))) {
    throw "请先把原程序复制为 $root\CursorQuotaPet.core.exe"
}

$iconArgument = @()
if (Test-Path -LiteralPath $icon) {
    $iconArgument = @("/win32icon:$icon")
}

& $compiler /nologo /target:winexe /optimize+ /debug- `
    /out:$output `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    @iconArgument `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "热修复启动层编译失败：exit $LASTEXITCODE"
}

Write-Output "已生成：$output"
