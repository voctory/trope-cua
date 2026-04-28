# Installation

Install Trope CUA from this repository. Windows and macOS are separate native installs.

## Windows Requirements

- Windows 10 1903+ or Windows 11.
- PowerShell.
- .NET SDK matching `global.json`.
- Python with `pytest` when running the integration tests.

The project targets .NET for Windows and publishes a single user-level executable.

## Windows Build

From the repository root:

```powershell
.\scripts\build.ps1
```

The published executable is written to:

```text
artifacts\publish\trope-cua.exe
```

Use `-SelfContained` when the published build should carry its own runtime:

```powershell
.\scripts\build.ps1 -SelfContained
```

## Windows Install

Install a self-contained user-level build:

```powershell
.\scripts\install-windows.ps1 -SelfContained
```

The installer publishes the binary and copies it to:

```text
%LOCALAPPDATA%\Programs\TropeCUA\trope-cua.exe
```

The Windows installer also copies the `trope-cua` skill into detected harness skill directories when they already exist.

Verify the installed binary:

```powershell
& "$env:LOCALAPPDATA\Programs\TropeCUA\trope-cua.exe" --help
& "$env:LOCALAPPDATA\Programs\TropeCUA\trope-cua.exe" list_windows
```

## Register with an MCP client

Trope CUA speaks MCP over stdio. Register the installed executable with any MCP-capable client:

```json
{
  "mcpServers": {
    "trope-cua": {
      "command": "C:\\Users\\YOU\\AppData\\Local\\Programs\\TropeCUA\\trope-cua.exe",
      "args": ["mcp"]
    }
  }
}
```

Plain MCP sessions without `--instance` automatically claim a runtime cursor identity and palette. The first live cursor uses `default_blue`; later live sessions rotate through 9 alternate palettes. Set `TROPE_CUA_INSTANCE` or pass `--instance` only when you need a stable named cursor identity.

## macOS Install

The macOS native driver lives under `native/macos/trope-cua` and keeps its own Swift package and scripts.

```bash
./scripts/install-macos.sh
```

This builds and installs the local checkout, which is the right path for testing branch changes.
Use `./scripts/install-macos-release.sh` only when you want the published macOS release installer.

You can also run the platform script directly:

```bash
cd native/macos/trope-cua
./scripts/install-local.sh
```

macOS builds and permission checks must be run on macOS.

## Run the daemon

Use the daemon when shell calls need to share the same element cache:

```powershell
trope-cua serve
```

In another terminal:

```powershell
trope-cua call get_window_state '{"pid":1234,"window_id":456789}'
trope-cua call click '{"pid":1234,"window_id":456789,"element_index":14}'
```

For parallel daemon-based agents, give each daemon an instance id:

```powershell
trope-cua serve --instance agent-a
trope-cua serve --instance agent-b

trope-cua call --instance agent-a list_windows '{}'
trope-cua call --instance agent-b list_windows '{}'
```

Inspect and stop daemons:

```powershell
trope-cua daemon-list
trope-cua status --instance agent-a
trope-cua stop --instance agent-a
trope-cua stop --all
```

## Config directory

By default, config and daemon registry data live under:

```text
%LOCALAPPDATA%\trope-cua
```

Set `TROPE_CUA_CONFIG_DIR` to isolate test runs, sandboxes, or multiple harnesses:

```powershell
$env:TROPE_CUA_CONFIG_DIR = "$env:TEMP\trope-cua-dev"
```

## Permissions and Windows behavior

Trope CUA uses ordinary same-session Windows APIs by default. There is no macOS-style TCC grant flow, but the tool can still be constrained by Windows integrity levels, desktop isolation, UAC, remote desktop policy, and app-specific UIA behavior.

Run:

```powershell
trope-cua check_permissions
```

Use the result as a diagnostic, not as a blanket guarantee that every target app is automatable. Elevated apps, protected surfaces, and hardware-input-only apps may need a different lane.

## Uninstall

```powershell
.\scripts\uninstall.ps1
```

You can also remove the installed directory manually:

```powershell
Remove-Item "$env:LOCALAPPDATA\Programs\TropeCUA" -Recurse -Force
```
