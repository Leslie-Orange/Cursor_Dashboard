param([switch]$TestScope)
$ErrorActionPreference = "Stop"

$codings = Split-Path -Parent $MyInvocation.MyCommand.Path
$packages = Join-Path (Split-Path -Parent $codings) "Packages"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$payloadExe = Join-Path $codings "CursorQuotaPet.exe"
$payloadIcon = Join-Path $codings "CursorQuotaPet.ico"
$installer = Join-Path $codings "Installer.cs"
$uninstaller = Join-Path $codings "Uninstaller.cs"
$manifest = Join-Path $codings "app.manifest"
$output = Join-Path $packages "CursorQuotaPet-Setup.exe"
$uninstallOutput = Join-Path $packages "CursorQuotaPet-Uninstall.exe"
$define = @()
if ($TestScope) {
    $output = Join-Path $packages "CursorQuotaPet-Setup.Test.exe"
    $uninstallOutput = Join-Path $packages "CursorQuotaPet-Uninstall.Test.exe"
    $define = @('/define:UNINSTALL_TEST')
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "找不到 .NET Framework C# 编译器：$compiler"
}
foreach ($required in @($payloadExe, $payloadIcon, $installer, $uninstaller, $manifest)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "缺少打包文件：$required"
    }
}

New-Item -ItemType Directory -Force -Path $packages | Out-Null
& $compiler /nologo /target:winexe /optimize+ /codepage:65001 @define `
    /out:$uninstallOutput /main:UninstallProgram `
    /win32icon:$payloadIcon /win32manifest:$manifest `
    /reference:System.dll /reference:System.Windows.Forms.dll $uninstaller
if ($LASTEXITCODE -ne 0) { throw "卸载程序编译失败：exit $LASTEXITCODE" }
$compileOutput = $output
if (Test-Path -LiteralPath $output) {
    try {
        Remove-Item -LiteralPath $output -Force
    }
    catch {
        $compileOutput = Join-Path $packages "CursorQuotaPet-Setup.new.exe"
    }
}

& $compiler /nologo /target:winexe /optimize+ /codepage:65001 /main:SetupProgram @define `
    /out:$compileOutput `
    /win32icon:$payloadIcon `
    /win32manifest:$manifest `
    /resource:"${payloadExe},CursorQuotaPet.exe" `
    /resource:"${payloadIcon},CursorQuotaPet.ico" `
    /resource:"${uninstallOutput},Uninstall.exe" `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $installer $uninstaller

if ($LASTEXITCODE -ne 0) {
    throw "安装包编译失败：exit $LASTEXITCODE"
}

if ($compileOutput -ne $output) {
    Move-Item -LiteralPath $compileOutput -Destination $output -Force
}

Write-Output "已生成：$output"
