# Sets user env vars used by per-module launchSettings.json profiles.
# Run once (restart Rider afterwards so it picks up the new vars).
#
# Usage:
#   .\setup-dev-env.ps1
#   .\setup-dev-env.ps1 -DesktopExe "C:\path\to\OpenShock.Desktop.exe"

[CmdletBinding()]
param(
    [string]$DesktopExe
)

$ErrorActionPreference = 'Stop'

if (-not $DesktopExe) {
    $repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $guess = Join-Path $repoRoot '..\Desktop\Desktop\bin\Debug-Windows\net10.0-windows10.0.19041.0\win-x64\OpenShock.Desktop.exe'
    if (Test-Path $guess) {
        $DesktopExe = (Resolve-Path $guess).Path
    } else {
        Write-Error "Could not auto-detect OpenShock.Desktop.exe at '$guess'. Pass -DesktopExe <path>."
    }
}

if (-not (Test-Path $DesktopExe)) {
    Write-Error "File not found: $DesktopExe"
}

$DesktopDir = Split-Path -Parent $DesktopExe

[Environment]::SetEnvironmentVariable('OPENSHOCK_DESKTOP_EXE', $DesktopExe, 'User')
[Environment]::SetEnvironmentVariable('OPENSHOCK_DESKTOP_DIR', $DesktopDir, 'User')

Write-Host "Set user env vars:"
Write-Host "  OPENSHOCK_DESKTOP_EXE = $DesktopExe"
Write-Host "  OPENSHOCK_DESKTOP_DIR = $DesktopDir"
Write-Host ""
Write-Host "Restart Rider for the new variables to take effect."