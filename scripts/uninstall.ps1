param(
  [string]$InstallDir = "$env:LOCALAPPDATA\Programs\CuaDriverWin"
)

if (Test-Path $InstallDir) {
  Remove-Item -Recurse -Force $InstallDir
}

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$newPath = (($userPath -split ';') | Where-Object { $_ -and ($_ -ne $InstallDir) }) -join ';'
[Environment]::SetEnvironmentVariable("Path", $newPath, "User")

Write-Host "Removed cua-driver-win."
