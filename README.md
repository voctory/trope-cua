# Trope CUA

Trope CUA is a Windows background computer-use driver for agent harnesses. It exposes a UI Automation tree, target-window screenshots, and cursor-safe actions over MCP, a daemon, or direct CLI calls.

The binary is currently named `cua-driver-win.exe` for compatibility.

## Why Use It

- Drive Windows apps without treating the user's real cursor or foreground app as the automation lane.
- Use `get_window_state` to combine screenshots with stable UIA `element_index` targets.
- Prefer background-safe UIA, MSAA, targeted `HWND`, and Chromium CDP routes before any hard-case lane.
- Show agent intent with a click-through visual cursor overlay, including distinct colors for parallel sessions.

## Install

Requirements: Windows 10 1903+ or Windows 11, PowerShell, and the .NET SDK pinned by `global.json`.

```powershell
.\scripts\install.ps1 -SelfContained
```

The installed binary is written to:

```text
%LOCALAPPDATA%\Programs\CuaDriverWin\cua-driver-win.exe
```

## MCP

Register the installed binary with your MCP client:

```json
{
  "mcpServers": {
    "trope-cua": {
      "command": "C:\\Users\\YOU\\AppData\\Local\\Programs\\CuaDriverWin\\cua-driver-win.exe",
      "args": ["mcp"]
    }
  }
}
```

## CLI Quickstart

```powershell
cua-driver-win list_windows
cua-driver-win get_window_state '{"pid":1234,"window_id":456789}'
cua-driver-win click '{"pid":1234,"window_id":456789,"element_index":14}'
```

For shell workflows that need persistent element caches:

```powershell
cua-driver-win serve --instance demo
cua-driver-win call --instance demo get_window_state '{"pid":1234,"window_id":456789}'
cua-driver-win call --instance demo click '{"pid":1234,"window_id":456789,"element_index":14}'
```

## Documentation

- [Documentation index](docs/README.md)
- [What is Trope CUA?](docs/introduction.md)
- [Installation](docs/installation.md)
- [Quickstart](docs/quickstart.md)
- [Tools and command modes](docs/reference-tools.md)
- [Known limits](docs/limits.md)

## Development

```powershell
.\scripts\build.ps1
dotnet format --verify-no-changes --no-restore --verbosity minimal
.\scripts\run-tests.ps1
```

Contributor notes live in [CONTRIBUTING.md](CONTRIBUTING.md). Design and routing details live under [docs/](docs/).

## License

Trope CUA is released under the MIT License. See [LICENSE](LICENSE).

This Windows driver was informed by the Cua Driver Mac implementation in [trycua/cua](https://github.com/trycua/cua), but does not include upstream source code from that repository. See [NOTICE](NOTICE). This project is not affiliated with or endorsed by Cua AI, Inc.
