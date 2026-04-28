# Trope CUA

Trope CUA is a native computer-use driver for agent harnesses. It exposes app/window state, screenshots, and cursor-safe actions over MCP, a daemon, or direct CLI calls.

Agents can drive background windows without using the user's hardware cursor as the automation lane. The visible cursor is a click-through overlay, so parallel agent sessions can run with separate cursor colors while the user keeps working.

## Platforms

- Windows: current production implementation at the repository root.
- macOS: native Swift implementation under `native/macos/trope-cua`.

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
