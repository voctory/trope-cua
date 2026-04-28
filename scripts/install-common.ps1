function Get-CuaDriverConfigDirectory {
  if ([string]::IsNullOrWhiteSpace($env:TROPE_CUA_CONFIG_DIR)) {
    return Join-Path $env:LOCALAPPDATA "trope-cua"
  }

  return [System.IO.Path]::GetFullPath($env:TROPE_CUA_CONFIG_DIR)
}

function Get-CuaDriverDefaultRuntime {
  $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
  if ($null -eq $architecture) {
    $architecture = [System.Runtime.InteropServices.Architecture]::X64
  }

  return "win-$($architecture.ToString().ToLowerInvariant())"
}

function Get-CuaDriverRequiredSdkVersion {
  param(
    [string]$Root
  )

  $GlobalJson = Join-Path $Root "global.json"
  if (-not (Test-Path $GlobalJson)) {
    return $null
  }

  return (Get-Content -LiteralPath $GlobalJson -Raw | ConvertFrom-Json).sdk.version
}

function Test-CuaDriverDotnetHasSdk {
  param(
    [string]$DotnetPath,
    [string]$SdkVersion
  )

  if ([string]::IsNullOrWhiteSpace($DotnetPath) -or -not (Test-Path $DotnetPath)) {
    if (-not (Get-Command $DotnetPath -ErrorAction SilentlyContinue)) {
      return $false
    }
  }

  $installedSdks = @(& $DotnetPath --list-sdks 2>$null)
  if ($LASTEXITCODE -ne 0) {
    return $false
  }

  return [bool]($installedSdks | Where-Object { $_ -match "^$([regex]::Escape($SdkVersion))\s+\[" } | Select-Object -First 1)
}

function Resolve-CuaDriverDotnet {
  param(
    [string]$Root
  )

  $requestedSdk = Get-CuaDriverRequiredSdkVersion -Root $Root
  if ([string]::IsNullOrWhiteSpace($requestedSdk)) {
    return "dotnet"
  }

  $candidates = @()
  if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
    $candidates += Join-Path $env:DOTNET_ROOT "dotnet.exe"
  }
  if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
    $candidates += Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
  }
  $candidates += "dotnet"

  foreach ($candidate in ($candidates | Select-Object -Unique)) {
    if (Test-CuaDriverDotnetHasSdk -DotnetPath $candidate -SdkVersion $requestedSdk) {
      return $candidate
    }
  }

  $installedSdks = @(dotnet --list-sdks 2>$null)
  $GlobalJson = Join-Path $Root "global.json"
  throw "Required .NET SDK $requestedSdk is not available to dotnet. Install it, add its dotnet.exe to PATH, set DOTNET_ROOT, or update $GlobalJson. Installed SDKs on PATH: $($installedSdks -join '; ')"
}

function Test-CuaDriverPathUnderDirectory {
  param(
    [string]$Path,
    [string]$Directory
  )

  if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($Directory)) {
    return $false
  }

  try {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetFullPath($Directory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    return $fullPath.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
  } catch {
    return $false
  }
}

function Stop-CuaDriverProcessesForInstallDir {
  param(
    [string]$InstallDir
  )

  $InstallRoot = [System.IO.Path]::GetFullPath($InstallDir)
  $InstalledExe = Join-Path $InstallRoot "trope-cua.exe"
  $RegistryDir = Join-Path (Get-CuaDriverConfigDirectory) "daemons"

  if ((Test-Path $InstalledExe) -and (Test-Path $RegistryDir)) {
    Get-ChildItem -LiteralPath $RegistryDir -Filter "*.json" -ErrorAction SilentlyContinue | ForEach-Object {
      try {
        $record = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        if (Test-CuaDriverPathUnderDirectory -Path $record.exePath -Directory $InstallRoot) {
          & $InstalledExe daemon-stop --instance $record.instanceId *> $null
        }
      } catch {
      }
    }
  }

  Get-Process trope-cua -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Test-CuaDriverPathUnderDirectory -Path $_.Path -Directory $InstallRoot) } |
    Stop-Process -Force
}
