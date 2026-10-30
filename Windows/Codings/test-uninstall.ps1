param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$codings = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SkipBuild) { & (Join-Path $codings 'build-package.ps1') -TestScope }
$setup = Join-Path (Split-Path -Parent $codings) 'Packages\CursorQuotaPet-Setup.Test.exe'
$testId = [Guid]::NewGuid().ToString('N')
$env:CQP_UNINSTALL_TEST_ID = $testId
$scope = Join-Path ([IO.Path]::GetTempPath()) "CursorQuotaPet-UninstallTest-$testId"
$registry = "HKCU:\Software\CursorQuotaPet\UninstallTests\$testId"
$canonical = "$registry\CursorQuotaPet"
$env:CQP_UNINSTALL_TEST_RESULT = Join-Path $scope 'result.txt'
function Assert-True($value, [string]$message) { if (-not $value) { throw $message } }
function Install-Test([string]$path) {
    $process = Start-Process -FilePath $setup -ArgumentList "--quiet --skip-process-stop --root `"$path`"" -Wait -PassThru -WindowStyle Hidden
    Assert-True ($process.ExitCode -eq 0) 'isolated installation failed'
}
function Uninstall-Test([string]$path) {
    Remove-Item -LiteralPath $env:CQP_UNINSTALL_TEST_RESULT -Force -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath (Join-Path $path 'Uninstall.exe') -ArgumentList '--quiet' -Wait -PassThru -WindowStyle Hidden
    Assert-True ($process.ExitCode -eq 0) 'uninstaller failed to launch cleanup helper'
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    while (-not (Test-Path -LiteralPath $env:CQP_UNINSTALL_TEST_RESULT)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'cleanup helper timed out' }
        Start-Sleep -Milliseconds 100
    }
    Assert-True ((Get-Content -LiteralPath $env:CQP_UNINSTALL_TEST_RESULT -Raw) -eq 'ok') 'cleanup helper failed'
}
try {
    New-Item -ItemType Directory -Path $scope -Force | Out-Null
    $root = Join-Path $scope 'Install With Spaces'
    $sameRootLegacy = "$registry\{TEST-SAME-ROOT}_is1"
    $otherRootLegacy = "$registry\{TEST-OTHER-ROOT}_is1"
    foreach ($legacy in @($sameRootLegacy, $otherRootLegacy)) {
        New-Item -Path $legacy -Force | Out-Null
        Set-ItemProperty -LiteralPath $legacy -Name DisplayName -Value 'Cursor仪表盘'
    }
    Set-ItemProperty -LiteralPath $sameRootLegacy -Name InstallLocation -Value ($root.ToLowerInvariant() + '\')
    Set-ItemProperty -LiteralPath $otherRootLegacy -Name InstallLocation -Value (Join-Path $scope 'Old Different Installation')
    Install-Test $root
    Assert-True (-not (Test-Path -LiteralPath $sameRootLegacy)) 'same-root legacy registration was not migrated'
    Assert-True (Test-Path -LiteralPath $otherRootLegacy) 'different-root legacy registration was deleted'
    $key = Get-ItemProperty -LiteralPath $canonical
    Assert-True ($key.UninstallString -eq ('"' + (Join-Path $root 'Uninstall.exe') + '"')) 'invalid UninstallString'
    Assert-True ($key.QuietUninstallString -eq ($key.UninstallString + ' --quiet')) 'invalid QuietUninstallString'
    Assert-True ($key.NoRemove -eq 0 -and $key.SystemComponent -eq 0) 'system uninstall is disabled'
    Assert-True (Test-Path -LiteralPath ($key.DisplayIcon -replace ',0$', '')) 'registered icon is missing'
    Uninstall-Test $root
    Assert-True (-not (Test-Path -LiteralPath $root)) 'empty install directory was not removed'
    Assert-True (-not (Test-Path -LiteralPath $canonical)) 'owned canonical key was not removed'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $scope 'Programs\Cursor仪表盘.lnk'))) 'owned start-menu shortcut was not removed'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $scope 'Desktop\Cursor仪表盘.lnk'))) 'owned desktop shortcut was not removed'

    # A locked owned file must return failure and keep the retryable registration.
    Install-Test $root
    $lockedIcon = [IO.File]::Open((Join-Path $root 'CursorQuotaPet.ico'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $process = Start-Process -FilePath (Join-Path $root 'Uninstall.exe') -ArgumentList '--quiet' -Wait -PassThru -WindowStyle Hidden
        Assert-True ($process.ExitCode -ne 0) 'locked-file uninstall reported success'
        Assert-True (Test-Path -LiteralPath $canonical) 'failed uninstall removed its retryable registration'
        Assert-True (Test-Path -LiteralPath (Join-Path $root 'Uninstall.exe')) 'failed uninstall removed its retryable uninstaller'
        Assert-True (Test-Path -LiteralPath (Join-Path $root '.cursorquotapet-install')) 'failed uninstall removed its ownership marker'
    }
    finally { $lockedIcon.Dispose() }
    Uninstall-Test $root

    Install-Test $root
    $sentinel = Join-Path $root 'keep-user-file.txt'
    Set-Content -LiteralPath $sentinel -Value 'keep'
    $foreign = Join-Path $scope 'Other Application'
    New-Item -ItemType Directory -Path $foreign -Force | Out-Null
    $foreignExe = Join-Path $foreign 'Cursor.exe'
    Set-Content -LiteralPath $foreignExe -Value 'editor sentinel'
    $desktop = Join-Path $scope 'Desktop\Cursor仪表盘.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($desktop)
    $shortcut.TargetPath = $foreignExe
    $shortcut.Save()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    Set-ItemProperty -LiteralPath $canonical -Name InstallLocation -Value $foreign
    Uninstall-Test $root
    Assert-True (Test-Path -LiteralPath $sentinel) 'unknown file was deleted'
    Assert-True (Test-Path -LiteralPath $foreignExe) 'foreign application was deleted'
    Assert-True (Test-Path -LiteralPath $desktop) 'foreign shortcut was deleted'
    Assert-True ((Get-ItemProperty -LiteralPath $canonical).InstallLocation -eq $foreign) 'foreign registration was removed'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'CursorQuotaPet.exe'))) 'owned application file remains'

    # A copied uninstaller cannot delete an unmarked directory or accept --root.
    $invalid = Join-Path $scope 'Unmarked'
    New-Item -ItemType Directory -Path $invalid -Force | Out-Null
    $testUninstaller = Join-Path (Split-Path -Parent $codings) 'Packages\CursorQuotaPet-Uninstall.Test.exe'
    Copy-Item -LiteralPath $testUninstaller -Destination (Join-Path $invalid 'Uninstall.exe')
    Set-Content -LiteralPath (Join-Path $invalid 'CursorQuotaPet.exe') -Value 'keep'
    foreach ($arguments in @('--quiet', ('--quiet --root "' + $foreign + '"'))) {
        $process = Start-Process -FilePath (Join-Path $invalid 'Uninstall.exe') -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        Assert-True ($process.ExitCode -ne 0) 'unsafe uninstall invocation succeeded'
    }
    Assert-True (Test-Path -LiteralPath (Join-Path $invalid 'CursorQuotaPet.exe')) 'unmarked app was deleted'
    Write-Output 'PASS: isolated install/uninstall, same-root migration, foreign legacy preservation, locked-file failure/retry, shortcuts, unknown files, foreign paths, and rejected root override'
}
finally {
    # The scope and registry namespace were generated by this test only.
    if (Test-Path -LiteralPath $registry) { Remove-Item -LiteralPath $registry -Recurse -Force }
    $resolvedScope = [IO.Path]::GetFullPath($scope)
    $expectedScope = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "CursorQuotaPet-UninstallTest-$testId"))
    if ($resolvedScope -eq $expectedScope -and $resolvedScope.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) {
        # Allow the already-finished helper to release its image handle.
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            try { if (Test-Path -LiteralPath $resolvedScope) { Remove-Item -LiteralPath $resolvedScope -Recurse -Force }; break }
            catch { if ($attempt -eq 19) { throw }; Start-Sleep -Milliseconds 100 }
        }
    }
    Remove-Item Env:\CQP_UNINSTALL_TEST_ID, Env:\CQP_UNINSTALL_TEST_RESULT -ErrorAction SilentlyContinue
}
