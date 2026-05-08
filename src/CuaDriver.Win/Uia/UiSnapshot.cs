namespace CuaDriver.Win.Uia;

internal sealed record UiSnapshot(
    int Pid,
    long WindowId,
    int TurnId,
    string TreeMarkdown,
    int ElementCount,
    IReadOnlyList<UiElementInfo> Elements,
    UiSnapshotMetrics Metrics,
    bool RequiredMatched);

internal sealed record UiSnapshotMetrics(
    long ElapsedMs,
    int ControlViewVisited,
    int RawViewVisited,
    int RawElementsAdded,
    bool RawHarvested,
    int MarkdownChars);
