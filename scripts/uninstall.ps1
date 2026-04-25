param(
  [string]$InstallDir = "$env:LOCALAPPDATA\Programs\CuaDriverWin"
)

$InstalledExe = Join-Path $InstallDir "cua-driver-win.exe"
if (Test-Path $InstalledExe) {
  try {
    & $InstalledExe daemon-stop --all | Out-Null
  } catch {
    try { & $InstalledExe daemon-stop | Out-Null } catch {}
  }
}

Get-Process cua-driver-win -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -and ($_.Path -like "$InstallDir*") } |
  Stop-Process -Force

if (Test-Path $InstallDir) {
  Remove-Item -Recurse -Force $InstallDir
}

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$newPath = (($userPath -split ';') | Where-Object { $_ -and ($_ -ne $InstallDir) }) -join ';'
[Environment]::SetEnvironmentVariable("Path", $newPath, "User")

Write-Host "Removed cua-driver-win."
