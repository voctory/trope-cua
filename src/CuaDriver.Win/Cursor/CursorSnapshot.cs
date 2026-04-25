namespace CuaDriver.Win.Cursor;

internal sealed record CursorSnapshot(
    bool Visible,
    int? ScreenX,
    int? ScreenY,
    long? TargetWindowId,
    string Layering,
    double? RenderFps,
    double? RenderMs,
    long RenderFrameCount);
