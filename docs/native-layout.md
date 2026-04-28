# Native Platform Layout

Trope CUA keeps the native drivers installable by platform.

## Windows

The Windows driver remains at the repository root:

- `src/CuaDriver.Win`
- `tests/integration`
- `scripts/build.ps1`
- `scripts/install.ps1`

Use the compatibility installer directly or the platform wrapper:

```powershell
.\scripts\install.ps1 -SelfContained
.\scripts\install-windows.ps1 -SelfContained
```

## macOS

The macOS driver lives under:

```text
native/macos/cua-driver
```

Use the platform wrapper from the repository root:

```bash
./scripts/install-macos.sh
```

The wrapper delegates to the macOS driver's own install script. This keeps the macOS Swift package self-contained while allowing Trope CUA to carry both native implementations in one repository.

## Compatibility

The Windows source has not been moved yet. That keeps existing CI, local scripts, and external references stable while macOS support is imported. A later reorganization can move Windows under `native/windows` once wrapper compatibility is in place for every public path.
