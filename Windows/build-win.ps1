$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$sourceDir = $PSScriptRoot
$buildDir = Join-Path $sourceDir ".build"
$outputExe = Join-Path $rootDir "CursorQuotaPet.exe"
$iconPath = Join-Path $sourceDir "app.ico"

$frameworkDir = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $frameworkDir "csc.exe"
if (-not (Test-Path $csc)) {
    $frameworkDir = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319"
    $csc = Join-Path $frameworkDir "csc.exe"
}
if (-not (Test-Path $csc)) {
    Write-Error "csc.exe not found. Install .NET Framework 4.8."
    exit 1
}

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$iconGen = Join-Path $buildDir "GenerateIcon.exe"
& $csc /nologo /codepage:65001 /target:exe /out:$iconGen /reference:System.Drawing.dll (Join-Path $sourceDir "GenerateIcon.cs")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $iconGen $iconPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$sources = @(
    (Join-Path $sourceDir "AssemblyInfo.cs"),
    (Join-Path $sourceDir "Program.cs"),
    (Join-Path $sourceDir "SqliteKv.cs"),
    (Join-Path $sourceDir "QuotaCore.cs"),
    (Join-Path $sourceDir "QuotaUi.cs")
)

$refs = @(
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Web.Extensions.dll"
)

$webDll = Join-Path $frameworkDir "System.Web.dll"
if (Test-Path $webDll) {
    $refs += "/reference:System.Web.dll"
}

$arguments = @(
    "/nologo",
    "/codepage:65001",
    "/utf8output",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/highentropyva+",
    "/win32icon:$iconPath",
    "/out:$outputExe"
) + $refs + $sources

& $csc @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Built: $outputExe"
