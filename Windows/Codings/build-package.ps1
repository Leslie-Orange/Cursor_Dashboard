$ErrorActionPreference = "Stop"

$codings = Split-Path -Parent $MyInvocation.MyCommand.Path
$packages = Join-Path (Split-Path -Parent $codings) "Packages"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$payloadExe = Join-Path $codings "CursorQuotaPet.exe"
$payloadIcon = Join-Path $codings "CursorQuotaPet.ico"
$installer = Join-Path $codings "Installer.cs"
$manifest = Join-Path $codings "app.manifest"
$output = Join-Path $packages "CursorQuotaPet-Setup.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "找不到 .NET Framework C# 编译器：$compiler"
}
foreach ($required in @($payloadExe, $payloadIcon, $installer, $manifest)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "缺少打包文件：$required"
    }
}

New-Item -ItemType Directory -Force -Path $packages | Out-Null
$compileOutput = $output
if (Test-Path -LiteralPath $output) {
    try {
        Remove-Item -LiteralPath $output -Force
    }
    catch {
        $compileOutput = Join-Path $packages "CursorQuotaPet-Setup.new.exe"
    }
}

& $compiler /nologo /target:winexe /optimize+ /codepage:65001 `
    /out:$compileOutput `
    /win32icon:$payloadIcon `
    /win32manifest:$manifest `
    /resource:"${payloadExe},CursorQuotaPet.exe" `
    /resource:"${payloadIcon},CursorQuotaPet.ico" `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $installer

if ($LASTEXITCODE -ne 0) {
    throw "安装包编译失败：exit $LASTEXITCODE"
}

if ($compileOutput -ne $output) {
    Move-Item -LiteralPath $compileOutput -Destination $output -Force
}

Write-Output "已生成：$output"
