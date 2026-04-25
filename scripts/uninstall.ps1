param(
  [string]$InstallDir = "$env:LOCALAPPDATA\Programs\CuaDriverWin"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "install-common.ps1")

Stop-CuaDriverProcessesForInstallDir -InstallDir $InstallDir

if (Test-Path $InstallDir) {
  Remove-Item -Recurse -Force $InstallDir
}

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$newPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
  ""
} else {
  (($userPath -split ';') | Where-Object { $_ -and ($_ -ne $InstallDir) }) -join ';'
}
[Environment]::SetEnvironmentVariable("Path", $newPath, "User")

Write-Host "Removed cua-driver-win."
