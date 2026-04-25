function Get-CuaDriverConfigDirectory {
  if ([string]::IsNullOrWhiteSpace($env:CUA_DRIVER_CONFIG_DIR)) {
    return Join-Path $env:LOCALAPPDATA "cua-driver-win"
  }

  return [System.IO.Path]::GetFullPath($env:CUA_DRIVER_CONFIG_DIR)
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
  $InstalledExe = Join-Path $InstallRoot "cua-driver-win.exe"
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

  Get-Process cua-driver-win -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Test-CuaDriverPathUnderDirectory -Path $_.Path -Directory $InstallRoot) } |
    Stop-Process -Force
}
