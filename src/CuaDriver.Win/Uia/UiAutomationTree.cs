using System.Text;
using System.Windows;
using System.Windows.Automation;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Uia;

internal sealed class UiAutomationTree
{
    private readonly object _gate = new();
    private readonly Dictionary<(int Pid, long WindowId), SessionState> _sessions = new();
    private int _nextTurnId;

    private sealed record SessionState(int TurnId, Dictionary<int, AutomationElement> Elements, UiSnapshot Snapshot);

    public UiSnapshot Snapshot(int pid, long windowId, string? query = null)
    {
        var hwnd = new IntPtr(windowId);
        var root = AutomationElement.FromHandle(hwnd)
                   ?? throw new InvalidOperationException($"No UI Automation element for HWND {windowId}.");

        var elements = new Dictionary<int, AutomationElement>();
        var infos = new List<UiElementInfo>();
        var sb = new StringBuilder();

        var turnId = Interlocked.Increment(ref _nextTurnId);
        Walk(root, root, windowId, pid, 0, 0, elements, infos, sb);

        var markdown = sb.ToString().TrimEnd();
        if (!string.IsNullOrWhiteSpace(query))
            markdown = UiTreeMarkdown.Filter(markdown, query!);

        var snapshot = new UiSnapshot(pid, windowId, turnId, markdown, elements.Count, infos);
        lock (_gate)
        {
            _sessions[(pid, windowId)] = new SessionState(turnId, elements, snapshot);
        }

        return snapshot;
    }

    public AutomationElement GetCachedElement(int pid, long windowId, int elementIndex)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue((pid, windowId), out var session))
                throw new InvalidOperationException($"No cached UIA state for pid {pid} window_id {windowId}. Call get_window_state first in the same MCP/daemon process.");

            if (!session.Elements.TryGetValue(elementIndex, out var element))
                throw new InvalidOperationException($"Invalid element_index {elementIndex} for pid {pid} window_id {windowId}.");

            return element;
        }
    }

    public UiSnapshot? GetLastSnapshot(int pid, long windowId)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue((pid, windowId), out var session) ? session.Snapshot : null;
        }
    }

    public static Rect? FindFirstDocumentBounds(long windowId)
    {
        try
        {
            var hwnd = new IntPtr(windowId);
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
                return null;
            var doc = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            if (doc is null)
                return null;
            var rect = doc.Current.BoundingRectangle;
            return rect.IsEmpty ? null : rect;
        }
        catch
        {
            return null;
        }
    }

    public static UiaHitTestResult? HitTest(int pid, long windowId, POINT screenPoint)
    {
        try
        {
            var root = AutomationElement.FromHandle(new IntPtr(windowId));
            if (root is null)
                return null;

            return HitTestWithinRoot(root, pid, windowId, screenPoint, wantScrollable: false);
        }
        catch
        {
            return null;
        }
    }

    public static UiaHitTestResult? FindScrollableAtPoint(int pid, long windowId, POINT screenPoint)
    {
        try
        {
            var root = AutomationElement.FromHandle(new IntPtr(windowId));
            if (root is null)
                return null;

            return HitTestWithinRoot(root, pid, windowId, screenPoint, wantScrollable: true);
        }
        catch
        {
            // Treat missing/vanishing UIA providers as no scrollable hit.
        }

        return null;
    }

    private static UiaHitTestResult? HitTestWithinRoot(AutomationElement root, int pid, long windowId, POINT screenPoint, bool wantScrollable)
    {
        var point = new System.Windows.Point(screenPoint.X, screenPoint.Y);
        UiaHitTestResult? bestClick = null;
        UiaHitTestResult? bestText = null;
        UiaHitTestResult? bestScrollable = null;
        var bestClickArea = double.MaxValue;
        var bestTextArea = double.MaxValue;
        var bestScrollableArea = double.MaxValue;

        Visit(root, depth: 0);
        return wantScrollable ? bestScrollable : bestClick ?? bestText;

        void Visit(AutomationElement element, int depth)
        {
            if (depth > 25)
                return;

            UiElementInfo? info = null;
            Rect rect = Rect.Empty;
            try
            {
                rect = element.Current.BoundingRectangle;
                if (rect.IsEmpty || !rect.Contains(point))
                    return;

                info = MakeInfo(element, 0, pid);
                if (info.ProcessId != pid && !IsSameWindowTree(element, root, windowId))
                    return;

                var area = Math.Max(1, rect.Width * rect.Height);
                if (wantScrollable && element.TryGetCurrentPattern(ScrollPattern.Pattern, out _) && area < bestScrollableArea)
                {
                    bestScrollableArea = area;
                    bestScrollable = new UiaHitTestResult(element, info.ControlType, info.Name, false, false, rect);
                }
                else if (!wantScrollable)
                {
                    var isTextInput = UiElementClassifier.IsTextInput(info);
                    if (UiElementClassifier.IsClickActionCandidate(info) && area < bestClickArea)
                    {
                        bestClickArea = area;
                        bestClick = new UiaHitTestResult(element, info.ControlType, info.Name, isTextInput, true, rect);
                    }
                    else if (isTextInput && area < bestTextArea)
                    {
                        bestTextArea = area;
                        bestText = new UiaHitTestResult(element, info.ControlType, info.Name, true, false, rect);
                    }
                }
            }
            catch
            {
                return;
            }

            var walker = TreeWalker.ControlViewWalker;
            AutomationElement? child = null;
            try { child = walker.GetFirstChild(element); } catch { }

            var ordinal = 0;
            while (child is not null && ordinal < 500)
            {
                Visit(child, depth + 1);
                try { child = walker.GetNextSibling(child); }
                catch { break; }
                ordinal++;
            }
        }
    }

    private static void Walk(
        AutomationElement element,
        AutomationElement root,
        long windowId,
        int pid,
        int depth,
        int siblingOrdinal,
        Dictionary<int, AutomationElement> cache,
        List<UiElementInfo> infos,
        StringBuilder sb)
    {
        if (depth > 25)
            return;

        UiElementInfo? info = null;
        try
        {
            info = MakeInfo(element, cache.Count, pid);
            if (info.ProcessId != 0 && info.ProcessId != pid && !IsSameWindowTree(element, root, windowId))
            {
                return;
            }

            var actionable = UiElementClassifier.IsActionable(info);
            if (actionable)
            {
                cache[info.ElementIndex] = element;
                infos.Add(info);
            }

            UiTreeMarkdown.AppendElement(sb, info, depth, actionable);
        }
        catch
        {
            // Element vanished or denied a property. Continue below if possible.
        }

        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? child = null;
        try { child = walker.GetFirstChild(element); } catch { }

        var ordinal = 0;
        while (child is not null && ordinal < 500)
        {
            Walk(child, root, windowId, pid, depth + 1, ordinal, cache, infos, sb);
            try { child = walker.GetNextSibling(child); }
            catch { break; }
            ordinal++;
        }
    }

    private static UiElementInfo MakeInfo(AutomationElement element, int proposedIndex, int fallbackPid)
    {
        var current = element.Current;
        var rect = Safe(() => current.BoundingRectangle);
        var patterns = Safe(() => element.GetSupportedPatterns().Select(PatternName).Distinct().OrderBy(x => x).ToArray()) ?? [];

        var processId = Safe(() => current.ProcessId);
        var nativeWindowHandle = Safe(() => current.NativeWindowHandle);
        var localizedType = Safe(() => current.LocalizedControlType);
        var programmaticType = Safe(() => current.ControlType.ProgrammaticName);

        return new UiElementInfo(
            proposedIndex,
            string.IsNullOrWhiteSpace(localizedType) ? (programmaticType ?? "element") : localizedType!,
            Safe(() => current.Name) ?? "",
            Safe(() => current.AutomationId) ?? "",
            Safe(() => current.ClassName) ?? "",
            RectDto.From(new RECT
            {
                Left = rect.IsEmpty ? 0 : (int)Math.Round(rect.X),
                Top = rect.IsEmpty ? 0 : (int)Math.Round(rect.Y),
                Right = rect.IsEmpty ? 0 : (int)Math.Round(rect.Right),
                Bottom = rect.IsEmpty ? 0 : (int)Math.Round(rect.Bottom)
            }),
            Safe(() => current.IsEnabled),
            Safe(() => current.IsOffscreen),
            processId == 0 ? fallbackPid : processId,
            nativeWindowHandle,
            patterns);
    }

    private static bool IsSameWindowTree(AutomationElement element, AutomationElement root, long windowId)
    {
        if (AutomationEquals(element, root))
            return true;

        var hwnd = Safe(() => element.Current.NativeWindowHandle);
        if (hwnd != 0)
        {
            var native = new IntPtr(hwnd);
            var rootHwnd = new IntPtr(windowId);
            if (native == rootHwnd || NativeMethods.IsChild(rootHwnd, native) || NativeMethods.GetAncestor(native, NativeMethods.GA_ROOT) == rootHwnd)
                return true;
        }

        var rect = Safe(() => root.Current.BoundingRectangle);
        var elementRect = Safe(() => element.Current.BoundingRectangle);
        return !rect.IsEmpty
               && !elementRect.IsEmpty
               && rect.Contains(new System.Windows.Point(
                   elementRect.X + Math.Min(2, elementRect.Width / 2),
                   elementRect.Y + Math.Min(2, elementRect.Height / 2)));
    }

    private static bool AutomationEquals(AutomationElement a, AutomationElement b)
    {
        try { return Automation.Compare(a, b); }
        catch { return false; }
    }

    private static string PatternName(AutomationPattern pattern)
    {
        var name = pattern.ProgrammaticName;
        name = name.Replace("PatternIdentifiers.Pattern", "", StringComparison.Ordinal);
        name = name.Replace("Pattern", "", StringComparison.Ordinal);
        name = name.Replace("Identifiers.", ".", StringComparison.Ordinal);
        return name.Trim('.');
    }

    private static T? Safe<T>(Func<T> f)
    {
        try { return f(); }
        catch { return default; }
    }
}
