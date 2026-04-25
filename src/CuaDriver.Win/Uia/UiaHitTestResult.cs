using System.Windows;
using System.Windows.Automation;

namespace CuaDriver.Win.Uia;

internal sealed record UiaHitTestResult(
    AutomationElement Element,
    string ControlType,
    string Name,
    bool IsTextInput,
    bool IsClickAction,
    Rect BoundingRectangle);
