param(
  [string]$InstallDir = "$env:LOCALAPPDATA\Programs\CuaDriverWin",
  [string]$Configuration = "Release",
  [string]$Runtime = "win-$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())",
  [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Runtime $Runtime -SelfContained:$SelfContained

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $Root "artifacts\publish\*") $InstallDir

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (($userPath -split ';') -notcontains $InstallDir) {
  [Environment]::SetEnvironmentVariable("Path", "$userPath;$InstallDir", "User")
  Write-Host "Added $InstallDir to user PATH. Restart your terminal."
}

Write-Host "Installed cua-driver-win to $InstallDir"
