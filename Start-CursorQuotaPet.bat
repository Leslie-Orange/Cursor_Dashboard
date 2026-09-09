@echo off
setlocal
cd /d "%~dp0"

if not exist "%~dp0CursorQuotaPet.exe" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Windows\build-win.ps1"
  if errorlevel 1 (
    echo Build failed. Please install .NET Framework 4.8.
    pause
    exit /b 1
  )
)

if /I "%~1"=="--probe" (
  "%~dp0CursorQuotaPet.exe" --probe
  exit /b %ERRORLEVEL%
)

start "" "%~dp0CursorQuotaPet.exe"
endlocal
