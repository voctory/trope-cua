param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())",
  [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Runtime $Runtime -SelfContained:$SelfContained
$env:CUA_DRIVER_EXE = Join-Path $Root "artifacts\publish\cua-driver-win.exe"
python -m pytest (Join-Path $Root "tests\integration")
if ($LASTEXITCODE -ne 0) {
  throw "pytest failed with exit code $LASTEXITCODE"
}
