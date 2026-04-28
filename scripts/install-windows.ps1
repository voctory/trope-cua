param(
  [string]$InstallDir = "$env:LOCALAPPDATA\Programs\TropeCUA",
  [string]$Configuration = "Release",
  [string]$Runtime,
  [switch]$SelfContained,
  [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$argsForInstall = @(
  "-InstallDir", $InstallDir,
  "-Configuration", $Configuration
)

if (-not [string]::IsNullOrWhiteSpace($Runtime)) {
  $argsForInstall += @("-Runtime", $Runtime)
}

if ($SelfContained) {
  $argsForInstall += "-SelfContained"
}

if ($FrameworkDependent) {
  $argsForInstall += "-FrameworkDependent"
}

& (Join-Path $ScriptDir "install.ps1") @argsForInstall

& (Join-Path $ScriptDir "install-skills-windows.ps1")
