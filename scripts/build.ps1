param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())",
  [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\CuaDriver.Win\CuaDriver.Win.csproj"
$Out = Join-Path $Root "artifacts\publish"

New-Item -ItemType Directory -Force -Path $Out | Out-Null

$sc = if ($SelfContained) { "true" } else { "false" }
dotnet publish $Project -c $Configuration -r $Runtime --self-contained:$sc -p:RestoreLockedMode=true -o $Out
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published to $Out"
Write-Host "Run: $Out\cua-driver-win.exe list_windows"
