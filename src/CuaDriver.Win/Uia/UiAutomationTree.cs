using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Uia;

internal sealed class UiAutomationTree
{
    private const int RawHarvestElementThreshold = 256;
    private static readonly string[] BrowserChromeRootAutomationIds = ["nav-bar", "urlbar"];

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
        var metrics = new SnapshotMetricsBuilder();
        var stopwatch = Stopwatch.StartNew();

        var turnId = Interlocked.Increment(ref _nextTurnId);
        Walk(root, root, windowId, pid, 0, 0, 0, elements, infos, sb, metrics);
        if (elements.Count < RawHarvestElementThreshold)
            HarvestRawElements(root, pid, elements, infos, sb, metrics);

        var markdown = sb.ToString().TrimEnd();
        if (!string.IsNullOrWhiteSpace(query))
            markdown = UiTreeMarkdown.Filter(markdown, query!);

        stopwatch.Stop();
        var snapshot = new UiSnapshot(
            pid,
            windowId,
            turnId,
            markdown,
            elements.Count,
            infos,
            metrics.Build(stopwatch.ElapsedMilliseconds, markdown.Length),
            RequiredMatched: true);
        lock (_gate)
        {
            _sessions[(pid, windowId)] = new SessionState(turnId, elements, snapshot);
        }

        return snapshot;
    }

    public UiSnapshot FindElements(
        int pid,
        long windowId,
        string? query,
        string? requiredQuery,
        string? automationId,
        string? identifier,
        string? controlType,
        string? targetZone,
        int limit,
        int maxVisited,
        bool includeOffscreen,
        CancellationToken cancellationToken)
    {
        var hwnd = new IntPtr(windowId);
        var root = AutomationElement.FromHandle(hwnd)
                   ?? throw new InvalidOperationException($"No UI Automation element for HWND {windowId}.");

        limit = Math.Clamp(limit, 1, 50);
        maxVisited = Math.Clamp(maxVisited, 50, 5000);

        var elements = new Dictionary<int, AutomationElement>();
        var infos = new List<UiElementInfo>();
        var sb = new StringBuilder();
        var metrics = new SnapshotMetricsBuilder();
        var stopwatch = Stopwatch.StartNew();
        var rootRect = Safe(() => root.Current.BoundingRectangle);
        var turnId = Interlocked.Increment(ref _nextTurnId);
        var normalizedQuery = NormalizeSearch(query);
        var normalizedRequiredQuery = NormalizeSearch(requiredQuery);
        var normalizedAutomationId = NormalizeSearch(automationId);
        var normalizedIdentifier = NormalizeSearch(identifier);
        var normalizedControlType = NormalizeControlType(controlType);
        var normalizedTargetZone = NormalizeSearch(targetZone);

        TryAddExactAutomationIdMatch(automationId);
        if (elements.Count < limit && !string.Equals(automationId, identifier, StringComparison.OrdinalIgnoreCase))
            TryAddExactAutomationIdMatch(identifier);

        var requiredMatched = normalizedRequiredQuery.Length == 0;

        foreach (var subtreeRoot in SelectSearchRoots(root, normalizedTargetZone, rootRect))
        {
            if (HasSatisfiedLimit())
                break;

            Visit(subtreeRoot, depth: 0);
        }

        stopwatch.Stop();
        if (!requiredMatched && elements.Count > 0)
        {
            elements.Clear();
            infos.Clear();
            sb.Clear();
        }

        var markdown = sb.ToString().TrimEnd();
        var snapshot = new UiSnapshot(
            pid,
            windowId,
            turnId,
            markdown,
            elements.Count,
            infos,
            metrics.Build(stopwatch.ElapsedMilliseconds, markdown.Length),
            requiredMatched);
        lock (_gate)
        {
            _sessions[(pid, windowId)] = new SessionState(turnId, elements, snapshot);
        }

        return snapshot;

        void Visit(AutomationElement element, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 35 || metrics.ControlViewVisited >= maxVisited || HasSatisfiedLimit())
                return;

            metrics.ControlViewVisited++;
            UiElementInfo? lightInfo = null;
            try
            {
                lightInfo = MakeInfo(element, elements.Count, pid, readPatterns: false);
                if (lightInfo.ProcessId != 0 && lightInfo.ProcessId != pid && !IsSameWindowTree(element, root, windowId))
                    return;

                requiredMatched |= MatchesRequiredContext(lightInfo, normalizedRequiredQuery);

                if ((!includeOffscreen && lightInfo.IsOffscreen)
                    || !IsInsideRoot(lightInfo.Bounds, rootRect)
                    || !MatchesSearch(lightInfo, normalizedQuery, normalizedAutomationId, normalizedIdentifier, normalizedControlType))
                {
                    // Keep walking children even when this node does not match.
                }
                else
                {
                    TryAddMatch(element);
                    if (HasSatisfiedLimit())
                        return;
                }
            }
            catch
            {
                // Element vanished or denied a property. Continue below if possible.
            }

            var walker = TreeWalker.ControlViewWalker;
            AutomationElement? child = null;
            try { child = walker.GetFirstChild(element); } catch { }

            var ordinal = 0;
            while (child is not null && ordinal < 1000 && metrics.ControlViewVisited < maxVisited && !HasSatisfiedLimit())
            {
                Visit(child, depth + 1);
                try { child = walker.GetNextSibling(child); }
                catch { break; }
                ordinal++;
            }
        }

        void TryAddExactAutomationIdMatch(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || elements.Count >= limit)
                return;

            try
            {
                var exact = root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        id.Trim(),
                        PropertyConditionFlags.IgnoreCase));
                if (exact is not null)
                    TryAddMatch(exact);
            }
            catch
            {
                // Browser providers sometimes reject optimized property queries.
            }
        }

        bool TryAddMatch(AutomationElement element)
        {
            if (IsAlreadyKnown(element, cache: elements))
                return false;

            var fullInfo = MakeInfo(element, elements.Count, pid);
            if (!UiElementClassifier.IsActionable(fullInfo))
                return false;

            if ((!includeOffscreen && fullInfo.IsOffscreen) || !IsInsideRoot(fullInfo.Bounds, rootRect))
                return false;

            elements[fullInfo.ElementIndex] = element;
            infos.Add(fullInfo);
            UiTreeMarkdown.AppendElement(sb, fullInfo, depth: 0, actionable: true);
            return true;
        }

        bool HasSatisfiedLimit() => elements.Count >= limit && requiredMatched;
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
        int renderedDepth,
        int siblingOrdinal,
        Dictionary<int, AutomationElement> cache,
        List<UiElementInfo> infos,
        StringBuilder sb,
        SnapshotMetricsBuilder metrics)
    {
        if (depth > 25)
            return;

        metrics.ControlViewVisited++;
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

            if (UiTreeMarkdown.ShouldRender(info, actionable, depth))
            {
                UiTreeMarkdown.AppendElement(sb, info, renderedDepth, actionable);
                renderedDepth++;
            }
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
            Walk(child, root, windowId, pid, depth + 1, renderedDepth, ordinal, cache, infos, sb, metrics);
            try { child = walker.GetNextSibling(child); }
            catch { break; }
            ordinal++;
        }
    }

    private static void HarvestRawElements(
        AutomationElement root,
        int pid,
        Dictionary<int, AutomationElement> cache,
        List<UiElementInfo> infos,
        StringBuilder sb,
        SnapshotMetricsBuilder metrics)
    {
        metrics.RawHarvested = true;
        var rootRect = Safe(() => root.Current.BoundingRectangle);
        if (rootRect.IsEmpty)
            return;

        var discovered = new List<(AutomationElement Element, UiElementInfo Info)>();
        var rawWalker = TreeWalker.RawViewWalker;
        var visited = 0;
        VisitRawChildren(root, depth: 0);

        if (discovered.Count == 0)
            return;

        sb.AppendLine("  - supplemental raw UIA elements");
        foreach (var (element, info) in discovered
                     .OrderBy(e => e.Info.Bounds.Y)
                     .ThenBy(e => e.Info.Bounds.X))
        {
            var indexedInfo = info with { ElementIndex = cache.Count };
            cache[indexedInfo.ElementIndex] = element;
            infos.Add(indexedInfo);
            metrics.RawElementsAdded++;
            UiTreeMarkdown.AppendElement(sb, indexedInfo, 2, actionable: true);
        }

        void TryRecord(AutomationElement element)
        {
            UiElementInfo info;
            try
            {
                info = MakeInfo(element, cache.Count + discovered.Count, pid);
            }
            catch
            {
                return;
            }

            if (!UiElementClassifier.IsActionable(info)
                || info.Bounds.Width <= 0
                || info.Bounds.Height <= 0
                || info.IsOffscreen
                || !IsInsideRoot(info.Bounds, rootRect)
                || IsAlreadyKnown(element, info, cache, discovered))
            {
                return;
            }

            discovered.Add((element, info));
        }

        void VisitRawChildren(AutomationElement parent, int depth)
        {
            if (depth > 35 || visited >= 5000 || discovered.Count >= 500)
                return;

            AutomationElement? child = null;
            try { child = rawWalker.GetFirstChild(parent); } catch { }

            var ordinal = 0;
            while (child is not null && ordinal < 1000 && visited < 5000 && discovered.Count < 500)
            {
                visited++;
                metrics.RawViewVisited++;
                TryRecord(child);
                VisitRawChildren(child, depth + 1);

                try { child = rawWalker.GetNextSibling(child); }
                catch { break; }
                ordinal++;
            }
        }
    }

    private static bool IsInsideRoot(RectDto bounds, Rect rootRect)
    {
        var x = bounds.X + Math.Min(2, bounds.Width / 2);
        var y = bounds.Y + Math.Min(2, bounds.Height / 2);
        return rootRect.Contains(new System.Windows.Point(x, y));
    }

    private static bool IsAlreadyKnown(
        AutomationElement element,
        UiElementInfo info,
        Dictionary<int, AutomationElement> cache,
        List<(AutomationElement Element, UiElementInfo Info)> discovered)
    {
        foreach (var existing in cache.Values)
        {
            if (AutomationEquals(existing, element))
                return true;
        }

        foreach (var (existing, existingInfo) in discovered)
        {
            if (AutomationEquals(existing, element)
                || (string.Equals(existingInfo.AutomationId, info.AutomationId, StringComparison.Ordinal)
                    && string.Equals(existingInfo.Name, info.Name, StringComparison.Ordinal)
                    && existingInfo.Bounds == info.Bounds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAlreadyKnown(
        AutomationElement element,
        Dictionary<int, AutomationElement> cache)
    {
        foreach (var existing in cache.Values)
        {
            if (AutomationEquals(existing, element))
                return true;
        }

        return false;
    }

    private static AutomationElement[] SelectSearchRoots(
        AutomationElement root,
        string targetZone,
        Rect rootRect)
    {
        if (targetZone is "page content" or "page" or "content" or "document" or "web page")
        {
            var documents = FindAllByControlType(root, ControlType.Document)
                .Where(element => IsUsableSearchRoot(element, rootRect))
                .ToArray();
            if (documents.Length > 0)
                return documents;
        }

        if (targetZone is "browser chrome" or "chrome" or "toolbar" or "address bar")
        {
            var chromeRoots = BrowserChromeRootAutomationIds
                .Select(id => FindFirstByAutomationId(root, id))
                .Where(static element => element is not null)
                .Cast<AutomationElement>()
                .Where(element => IsUsableSearchRoot(element, rootRect))
                .ToArray();
            if (chromeRoots.Length > 0)
                return chromeRoots;
        }

        return [root];
    }

    private static IEnumerable<AutomationElement> FindAllByControlType(AutomationElement root, ControlType controlType)
    {
        AutomationElementCollection? elements = null;
        try
        {
            elements = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
        }
        catch
        {
            yield break;
        }

        foreach (AutomationElement element in elements)
            yield return element;
    }

    private static AutomationElement? FindFirstByAutomationId(AutomationElement root, string automationId)
    {
        try
        {
            return root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.AutomationIdProperty,
                    automationId,
                    PropertyConditionFlags.IgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUsableSearchRoot(AutomationElement element, Rect rootRect)
    {
        try
        {
            if (element.Current.IsOffscreen)
                return false;

            var rect = element.Current.BoundingRectangle;
            return !rect.IsEmpty &&
                   (rootRect.IsEmpty || rootRect.Contains(new System.Windows.Point(
                       rect.X + Math.Min(2, rect.Width / 2),
                       rect.Y + Math.Min(2, rect.Height / 2))));
        }
        catch
        {
            return false;
        }
    }

    private static UiElementInfo MakeInfo(
        AutomationElement element,
        int proposedIndex,
        int fallbackPid,
        bool readPatterns = true)
    {
        var current = element.Current;
        var rect = Safe(() => current.BoundingRectangle);

        var processId = Safe(() => current.ProcessId);
        var nativeWindowHandle = Safe(() => current.NativeWindowHandle);
        var localizedType = Safe(() => current.LocalizedControlType);
        var programmaticType = Safe(() => current.ControlType.ProgrammaticName);
        var controlType = string.IsNullOrWhiteSpace(localizedType) ? (programmaticType ?? "element") : localizedType!;
        var patterns = readPatterns && UiElementClassifier.ShouldReadPatterns(controlType)
            ? (Safe(() => element.GetSupportedPatterns().Select(PatternName).Distinct().OrderBy(x => x).ToArray()) ?? [])
            : [];
        var isPassword = Safe(() => current.IsPassword);
        var value = readPatterns && !isPassword && UiElementClassifier.ShouldReadValue(controlType)
            ? ReadElementValue(element)
            : "";

        return new UiElementInfo(
            proposedIndex,
            controlType,
            Safe(() => current.Name) ?? "",
            value,
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

    private static string ReadElementValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw) &&
                raw is ValuePattern valuePattern)
            {
                return NormalizeRenderedValue(valuePattern.Current.Value);
            }
        }
        catch
        {
            // Some providers expose ValuePattern but reject reads while the tree is moving.
        }

        try
        {
            var rawValue = element.GetCurrentPropertyValue(ValuePattern.ValueProperty, ignoreDefaultValue: true);
            if (rawValue is string text)
                return NormalizeRenderedValue(text);
        }
        catch
        {
            // Fall through to RangeValue for numeric controls.
        }

        try
        {
            if (element.TryGetCurrentPattern(RangeValuePattern.Pattern, out var raw) &&
                raw is RangeValuePattern range)
            {
                return range.Current.Value.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // Treat unreadable provider values as absent.
        }

        return "";
    }

    private static string NormalizeRenderedValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        const int maxRenderedValueLength = 512;
        return normalized.Length <= maxRenderedValueLength
            ? normalized
            : normalized[..maxRenderedValueLength];
    }

    private static bool MatchesSearch(
        UiElementInfo info,
        string query,
        string automationId,
        string identifier,
        string controlType)
    {
        var normalizedType = NormalizeControlType(info.ControlType);
        var controlTypeMatches = controlType.Length == 0 || ControlTypeMatches(normalizedType, controlType);

        var normalizedName = NormalizeSearch(info.Name);
        var normalizedValue = NormalizeSearch(info.Value);
        var normalizedId = NormalizeSearch(info.AutomationId);
        var normalizedClass = NormalizeSearch(info.ClassName);
        var haystack = string.Join(' ', normalizedName, normalizedValue, normalizedId, normalizedClass, normalizedType).Trim();

        var hasPrimaryHint = query.Length > 0 || automationId.Length > 0 || identifier.Length > 0;
        if (!hasPrimaryHint)
            return controlType.Length > 0 && controlTypeMatches;

        if (automationId.Length > 0 &&
            (normalizedId.Equals(automationId, StringComparison.Ordinal) ||
             normalizedId.Contains(automationId, StringComparison.Ordinal)))
            return true;

        if (identifier.Length > 0 &&
            (normalizedId.Contains(identifier, StringComparison.Ordinal) ||
             normalizedValue.Contains(identifier, StringComparison.Ordinal) ||
             normalizedName.Contains(identifier, StringComparison.Ordinal) ||
             normalizedClass.Contains(identifier, StringComparison.Ordinal)))
            return true;

        return query.Length > 0 && TextMatches(haystack, query);
    }

    private static bool MatchesRequiredContext(UiElementInfo info, string requiredQuery)
    {
        if (requiredQuery.Length == 0)
            return true;

        var normalizedName = NormalizeSearch(info.Name);
        var normalizedValue = NormalizeSearch(info.Value);
        var normalizedId = NormalizeSearch(info.AutomationId);
        var normalizedClass = NormalizeSearch(info.ClassName);
        var normalizedType = NormalizeControlType(info.ControlType);
        var haystack = string.Join(' ', normalizedName, normalizedValue, normalizedId, normalizedClass, normalizedType).Trim();
        return TextMatches(haystack, requiredQuery);
    }

    private static bool ControlTypeMatches(string candidate, string requested) =>
        candidate.Equals(requested, StringComparison.Ordinal)
        || candidate.Contains(requested, StringComparison.Ordinal)
        || requested.Contains(candidate, StringComparison.Ordinal);

    private static bool TextMatches(string haystack, string needle)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
            return true;

        var tokens = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(token => haystack.Contains(token, StringComparison.Ordinal));
    }

    private static string NormalizeControlType(string? value)
    {
        var normalized = NormalizeSearch(value);
        return normalized.StartsWith("controltype ", StringComparison.Ordinal)
            ? normalized["controltype ".Length..]
            : normalized;
    }

    private static string NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\([^)]*\)", " ");
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
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

    private sealed class SnapshotMetricsBuilder
    {
        public int ControlViewVisited { get; set; }
        public int RawViewVisited { get; set; }
        public int RawElementsAdded { get; set; }
        public bool RawHarvested { get; set; }

        public UiSnapshotMetrics Build(long elapsedMs, int markdownChars) => new(
            elapsedMs,
            ControlViewVisited,
            RawViewVisited,
            RawElementsAdded,
            RawHarvested,
            markdownChars);
    }
}
