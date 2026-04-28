param(
  [string]$Configuration = "Release",
  [string]$Runtime,
  [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "install-common.ps1")
if ([string]::IsNullOrWhiteSpace($Runtime)) {
  $Runtime = Get-CuaDriverDefaultRuntime
}
& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Runtime $Runtime -SelfContained:$SelfContained

$ProgramFilesDotnet = Join-Path $env:ProgramFiles "dotnet"
$ProgramFilesX64Dotnet = Join-Path $env:ProgramFiles "dotnet\x64"
if ($Runtime -eq "win-arm64" -and (Test-Path $ProgramFilesDotnet)) {
  $env:DOTNET_ROOT = $ProgramFilesDotnet
  $env:DOTNET_ROOT_ARM64 = $ProgramFilesDotnet
}
if ($Runtime -eq "win-x64" -and (Test-Path $ProgramFilesX64Dotnet)) {
  $env:DOTNET_ROOT = $ProgramFilesX64Dotnet
  $env:DOTNET_ROOT_X64 = $ProgramFilesX64Dotnet
}

$env:TROPE_CUA_EXE = Join-Path $Root "artifacts\publish\trope-cua.exe"
$TestsDir = Join-Path $Root "tests\integration"
try {
  python -m pytest $TestsDir
  if ($LASTEXITCODE -ne 0) {
    throw "pytest failed with exit code $LASTEXITCODE"
  }
} finally {
  $Pycache = Join-Path $TestsDir "__pycache__"
  if (Test-Path $Pycache) {
    Remove-Item -LiteralPath $Pycache -Recurse -Force
  }
}
