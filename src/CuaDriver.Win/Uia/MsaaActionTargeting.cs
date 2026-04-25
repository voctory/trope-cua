using System.Windows.Automation;

namespace CuaDriver.Win.Uia;

internal static class MsaaActionTargeting
{
    public static System.Windows.Rect PreferredActionRect(AutomationElement element)
    {
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty)
            return rect;

        var type = Safe(() => element.Current.LocalizedControlType) ?? "";
        if (!type.Contains("link", StringComparison.OrdinalIgnoreCase))
            return rect;

        var heading = FindDescendantRect(
            element,
            rect,
            depth: 0,
            candidate => candidate.Contains("heading", StringComparison.OrdinalIgnoreCase)
                         || candidate.Contains("header", StringComparison.OrdinalIgnoreCase));
        if (!heading.IsEmpty)
            return heading;

        var actionable = FindDescendantRect(
            element,
            rect,
            depth: 0,
            candidate => candidate.Contains("button", StringComparison.OrdinalIgnoreCase)
                         || candidate.Contains("text", StringComparison.OrdinalIgnoreCase));
        return actionable.IsEmpty ? rect : actionable;
    }

    private static System.Windows.Rect FindDescendantRect(
        AutomationElement element,
        System.Windows.Rect parent,
        int depth,
        Func<string, bool> predicate)
    {
        if (depth > 4)
            return System.Windows.Rect.Empty;

        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? child = null;
        try { child = walker.GetFirstChild(element); } catch { }

        while (child is not null)
        {
            try
            {
                var current = child.Current;
                var childRect = current.BoundingRectangle;
                var localized = current.LocalizedControlType ?? "";
                if (!childRect.IsEmpty
                    && childRect.Width > 4
                    && childRect.Height > 4
                    && parent.Contains(new System.Windows.Point(childRect.X + childRect.Width / 2, childRect.Y + childRect.Height / 2))
                    && predicate(localized))
                {
                    return childRect;
                }

                var nested = FindDescendantRect(child, parent, depth + 1, predicate);
                if (!nested.IsEmpty)
                    return nested;
            }
            catch
            {
                // Element vanished; continue siblings.
            }

            try { child = walker.GetNextSibling(child); }
            catch { break; }
        }

        return System.Windows.Rect.Empty;
    }

    private static T? Safe<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch
        {
            return default;
        }
    }
}
