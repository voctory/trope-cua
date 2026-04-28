# Trope CUA documentation

Trope CUA lets agents inspect and operate real desktop apps while avoiding accidental movement of the user's real cursor or changes to the active app when the OS and target app support safe routes. It exposes target-window screenshots, accessibility trees, and action receipts over MCP, a long-running daemon, or direct CLI calls.

This documentation is organized for two readers:

- Agent builders who need to wire Trope CUA into an MCP client or shell-based loop.
- Driver contributors who need to preserve the background-safety contract while changing routes, tools, capture, or cursor behavior.

## Guide

- [What is Trope CUA?](introduction.md)
- [Installation](installation.md)
- [Quickstart](quickstart.md)
- [Workflows](workflows.md)
- [macOS operations](macos.md)

## Reference

- [Tools and command modes](reference-tools.md)
- [Known limits](limits.md)
- [Threat model](threat-model.md)
- [Native platform layout](native-layout.md)
- [Platform API map](api-map.md)

## Engineering notes

- [Agent routing guide](agent-routing.md)
- [Tool output format](tool-output-format.md)
- [No-regression contract](no-regression-contract.md)
- [Design](design.md)
- [Capture](capture.md)
- [Child-session lane](child-session.md)
- [AppBroadcast/InputInjector lane](appbroadcast-inputinjector.md)
