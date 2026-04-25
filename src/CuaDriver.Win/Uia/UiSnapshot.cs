namespace CuaDriver.Win.Uia;

internal sealed record UiSnapshot(
    int Pid,
    long WindowId,
    int TurnId,
    string TreeMarkdown,
    int ElementCount,
    IReadOnlyList<UiElementInfo> Elements);
