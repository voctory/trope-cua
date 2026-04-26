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
$DotnetCliHome = Join-Path $Root "artifacts\.dotnet-cli-home"
New-Item -ItemType Directory -Force -Path $DotnetCliHome | Out-Null
$env:DOTNET_CLI_HOME = $DotnetCliHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$Dotnet = Resolve-CuaDriverDotnet -Root $Root
$Project = Join-Path $Root "src\CuaDriver.Win\CuaDriver.Win.csproj"
$Out = Join-Path $Root "artifacts\publish"

New-Item -ItemType Directory -Force -Path $Out | Out-Null

$sc = if ($SelfContained) { "true" } else { "false" }
& $Dotnet publish $Project -c $Configuration -r $Runtime --self-contained:$sc -p:RestoreLockedMode=true -o $Out
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published to $Out"
Write-Host "Run: $Out\cua-driver-win.exe list_windows"
