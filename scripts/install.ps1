param(
  [string]$InstallDir = "$env:LOCALAPPDATA\Programs\CuaDriverWin",
  [string]$Configuration = "Release",
  [string]$Runtime = "win-$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())",
  [switch]$SelfContained,
  [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "install-common.ps1")
$InstallSelfContained = $SelfContained -or -not $FrameworkDependent
& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Runtime $Runtime -SelfContained:$InstallSelfContained

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Stop-CuaDriverProcessesForInstallDir -InstallDir $InstallDir

Copy-Item -Recurse -Force (Join-Path $Root "artifacts\publish\*") $InstallDir

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (($userPath -split ';') -notcontains $InstallDir) {
  $newPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $InstallDir } else { "$userPath;$InstallDir" }
  [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
  Write-Host "Added $InstallDir to user PATH. Restart your terminal."
}

Write-Host "Installed cua-driver-win to $InstallDir"
