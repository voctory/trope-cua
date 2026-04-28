# Capture notes

The source drop includes `WindowCapture`, which attempts `PrintWindow(PW_RENDERFULLCONTENT)` and falls back to screen copy. This is useful for bring-up and UIA/SOM plumbing but is not the final capture path for occluded/minimized cases.

Production capture path:

1. Create a `GraphicsCaptureItem` for the target `HWND` through `IGraphicsCaptureItemInterop::CreateForWindow`.
2. Create a D3D11 device and `Direct3D11CaptureFramePool`.
3. Set `GraphicsCaptureSession.IsCursorCaptureEnabled = false`.
4. Request borderless capture when permitted and set `IsBorderRequired = false`.
5. Copy the frame to an encoded JPEG/PNG.

`get_window_state` defaults to `capture_mode=som` and returns the UIA tree plus a native-resolution JPEG screenshot. Use `capture_mode=ax` for cheap tree-only refreshes when pixels are not needed. Set `max_image_dimension` if a workflow needs to cap image payloads; use `zoom` for local detail.

Why this is a seam in this source drop:

- The implementation requires Windows-only WinRT/D3D interop packages and target-machine validation.
- Same-session semantics and minimized-window behavior differ by OS build and target app.
- The no-regression contract depends on verifying cursor exclusion and foreground stability on the actual target OS.

For hard-case apps, prefer running the whole target and driver inside the child-session lane, where capture and hardware-style input are both isolated from the parent user session.
