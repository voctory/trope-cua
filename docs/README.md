# Trope CUA documentation

Trope CUA is a native background computer-use driver for agent harnesses. It exposes target-window screenshots, accessibility trees, and cursor-safe action tools over MCP, a long-running daemon, or direct CLI calls.

This documentation is organized for two readers:

- Agent builders who need to wire Trope CUA into an MCP client or shell-based loop.
- Driver contributors who need to preserve the background-safety contract while changing routes, tools, capture, or cursor behavior.

## Guide

- [What is Trope CUA?](introduction.md)
- [Installation](installation.md)
- [Quickstart](quickstart.md)

## Reference

- [Tools and command modes](reference-tools.md)
- [Known limits](limits.md)
- [Native platform layout](native-layout.md)

## Engineering notes

- [Agent routing guide](agent-routing.md)
- [Tool output format](tool-output-format.md)
- [No-regression contract](no-regression-contract.md)
- [Design](design.md)
- [Capture](capture.md)
- [Child-session lane](child-session.md)
- [AppBroadcast/InputInjector lane](appbroadcast-inputinjector.md)
