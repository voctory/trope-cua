# Privacy

Trope CUA runs locally. By itself, the default driver does not send screenshots, accessibility trees, recordings, or telemetry to Trope.

Agents and MCP clients connected to Trope CUA may receive local window state, screenshots, accessibility trees, and tool receipts. Their own data handling depends on the harness you connect.

## What Can Be Captured

Depending on the tool call and capture mode, Trope CUA can expose:

- Top-level app and window metadata.
- Target-window screenshots.
- Accessibility trees and element labels.
- Cursor overlay state.
- Mutating action receipts.
- Trajectory files when recording is enabled.

Screenshots and accessibility trees can contain private information from the targeted desktop window.

## Trajectory Recording

Recording is off by default.

When enabled, subsequent mutating actions write files under the configured output directory. A recording can include action JSON, post-action app state, screenshots, and click markers.

Enable recording:

```powershell
trope-cua set_recording '{"enabled":true,"output_dir":"C:\\temp\\trope-cua-run"}'
```

Disable recording:

```powershell
trope-cua set_recording '{"enabled":false}'
```

Delete traces by deleting the recording directory you configured.

## Config and Local State

Windows config and daemon registry data live under:

```text
%LOCALAPPDATA%\trope-cua
```

The installed Windows executable lives under:

```text
%LOCALAPPDATA%\Programs\TropeCUA
```

macOS config lives under:

```text
~/Library/Application Support/Trope CUA
```

macOS daemon/cache state lives under:

```text
~/Library/Caches/trope-cua
```

## Telemetry

Trope CUA does not include product telemetry. Build scripts also set .NET CLI telemetry opt-out while publishing the Windows executable.

If you connect Trope CUA to an external agent service, model provider, MCP client, or logging system, review that system's privacy behavior separately.
