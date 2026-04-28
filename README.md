# Trope CUA

![Trope CUA: computer-use agents for Windows and macOS](assets/trope-cua-hero.png)

Trope CUA is a native computer-use driver for agent harnesses on Windows and macOS. It exposes app/window state, screenshots, and cursor-safe actions over MCP, a daemon, or direct CLI calls.

Agents can drive background windows without using the user's hardware cursor as the automation lane. The visible cursor is a click-through overlay, so parallel agent sessions can run with separate cursor colors while the user keeps working.

## Platforms

- Windows implementation at the repository root.
- macOS Swift implementation under `native/macos/trope-cua`.

Each platform installs independently.

```powershell
.\scripts\install-windows.ps1 -SelfContained
```

```bash
./scripts/install-macos.sh
```

`install-macos.sh` builds and installs this checkout. Use
`scripts/install-macos-release.sh` only when you want the published macOS
release installer.

Register with MCP using `trope-cua mcp-config`, or point your MCP client at `trope-cua.exe mcp` on Windows and `trope-cua mcp` on macOS.

## Documentation

- [Documentation index](docs/README.md)
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

## License

Trope CUA is released under the MIT License. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

## Trademarks

Apple, macOS, Microsoft, Windows, Ubuntu, Canonical, OpenAI, Anthropic, Claude, Cua, and other referenced names are trademarks or registered trademarks of their respective owners. This project is not affiliated with or endorsed by those companies or projects.
