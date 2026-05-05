namespace CuaDriver.Win.Uia;

internal static class UiElementClassifier
{
    public static bool IsActionable(UiElementInfo info)
    {
        if (info.Patterns.Count > 0)
            return true;

        return IsLikelyActionableControlType(info.ControlType);
    }

    public static bool IsLikelyActionableControlType(string controlType) =>
        IsLikelyActionableControlTypeCore(controlType.ToLowerInvariant());

    public static bool ShouldReadPatterns(string controlType)
    {
        var type = controlType.ToLowerInvariant();
        return IsLikelyActionableControlTypeCore(type)
               || type.Contains("check box")
               || type.Contains("radio button")
               || type.Contains("split button")
               || type.Contains("data item")
               || type.Contains("custom");
    }

    private static bool IsLikelyActionableControlTypeCore(string type)
    {
        return type.Contains("button")
               || type == "link"
               || type.Contains("edit")
               || type.Contains("hyperlink")
               || type.Contains("menu item")
               || type.Contains("list item")
               || type.Contains("tab item")
               || type.Contains("tree item")
               || type.Contains("combo box")
               || type.Contains("slider")
               || type.Contains("scroll bar")
               || type.Contains("document");
    }

    public static bool IsClickActionCandidate(UiElementInfo info)
    {
        var type = info.ControlType.ToLowerInvariant();
        if (type.Contains("button")
            || type.Contains("hyperlink")
            || type == "link"
            || type.Contains("menu item")
            || type.Contains("tab item")
            || type.Contains("list item")
            || type.Contains("tree item")
            || type.Contains("check box")
            || type.Contains("radio button")
            || type.Contains("combo box")
            || type.Contains("slider")
            || type.Contains("scroll bar"))
            return info.Patterns.Count > 0;

        return false;
    }

    public static bool IsTextInput(UiElementInfo info)
    {
        var type = info.ControlType.ToLowerInvariant();
        if (!type.Contains("edit"))
            return false;

        return info.Patterns.Any(p => p.Contains("Value", StringComparison.OrdinalIgnoreCase)
                                      || p.Contains("Text", StringComparison.OrdinalIgnoreCase));
    }
}
