$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$installerDir = Join-Path $rootDir "installer"
$iss = Join-Path $installerDir "CursorDashboard.iss"
$distDir = Join-Path $rootDir "dist"

$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) {
    $iscc = "C:\Program Files\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) {
    Write-Error "ISCC.exe not found. Install Inno Setup 6."
    exit 1
}

& (Join-Path $PSScriptRoot "build-win.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
& $iscc /Q $iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setup = Get-ChildItem $distDir -Filter "*Setup.exe" | Select-Object -First 1
if (-not $setup) {
    Write-Error "Installer was not created."
    exit 1
}

Write-Host "Installer: $($setup.FullName)"
