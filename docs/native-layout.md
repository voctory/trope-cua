# Native Platform Layout

Trope CUA keeps the native drivers installable by platform.

## Windows

The Windows driver remains at the repository root:

- `src/CuaDriver.Win`
- `tests/integration`
- `scripts/build.ps1`
- `scripts/install.ps1`

Use the installer directly or the platform wrapper:

```powershell
.\scripts\install.ps1 -SelfContained
.\scripts\install-windows.ps1 -SelfContained
```

## macOS

The macOS driver lives under:

```text
native/macos/trope-cua
```

Use the platform wrapper from the repository root:

```bash
./scripts/install-macos.sh
```

The wrapper delegates to the macOS driver's own install script. This keeps the macOS Swift package self-contained while allowing Trope CUA to carry both native implementations in one repository.

## Current Layout

The repository uses the Trope CUA package name only. The Windows implementation still lives at the repository root because the existing .NET solution, CI, and tests are rooted there. The macOS implementation is nested under `native/macos/trope-cua` because it remains a self-contained Swift package with its own scripts.
