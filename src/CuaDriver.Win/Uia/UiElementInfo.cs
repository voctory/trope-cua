using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Uia;

internal sealed record UiElementInfo(
    int ElementIndex,
    string ControlType,
    string Name,
    string AutomationId,
    string ClassName,
    RectDto Bounds,
    bool IsEnabled,
    bool IsOffscreen,
    int ProcessId,
    long NativeWindowHandle,
    IReadOnlyList<string> Patterns);
