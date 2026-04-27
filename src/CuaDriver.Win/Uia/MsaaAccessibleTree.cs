using System.Globalization;
using System.Windows;
using Accessibility;

namespace CuaDriver.Win.Uia;

internal static class MsaaAccessibleTree
{
    private const int ChildIdSelf = 0;
    private const int MaxTraversalNodes = 1500;

    private sealed class TraversalBudget
    {
        public int Visited { get; set; }
    }

    public static AccessibleHit? HitTestDeep(IAccessible current, int x, int y, int depth = 0)
    {
        if (depth > 12)
            return new AccessibleHit(current, ChildIdSelf);

        object? hit;
        try
        {
            hit = current.accHitTest(x, y);
        }
        catch
        {
            return new AccessibleHit(current, ChildIdSelf);
        }

        if (hit is null)
            return null;

        var childAccessible = Ia2ComQuery.AsAccessible(hit);
        if (childAccessible is not null)
            return HitTestDeep(childAccessible, x, y, depth + 1) ?? new AccessibleHit(childAccessible, ChildIdSelf);

        var childId = CoerceChildId(hit);
        if (childId is null)
            return new AccessibleHit(current, ChildIdSelf);

        if (childId.Value == ChildIdSelf)
            return new AccessibleHit(current, ChildIdSelf);

        var nested = TryGetChild(current, childId.Value);
        if (nested is not null)
            return HitTestDeep(nested, x, y, depth + 1) ?? new AccessibleHit(nested, ChildIdSelf);

        return new AccessibleHit(current, childId.Value);
    }

    public static AccessibleHit PromoteClickAncestor(AccessibleHit hit)
    {
        var current = hit;
        for (var depth = 0; depth < 12; depth++)
        {
            var action = DefaultAction(current);
            if (!string.IsNullOrWhiteSpace(action)
                && !action.Equals("clickAncestor", StringComparison.OrdinalIgnoreCase)
                && !action.Equals("click ancestor", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            var parent = TryGetParent(current.Accessible);
            if (parent is null)
                return current;

            current = new AccessibleHit(parent, ChildIdSelf);
        }

        return current;
    }

    public static IEnumerable<AccessibleHit> AncestorsAndSelf(AccessibleHit hit, int maxDepth = 8)
    {
        yield return hit;

        var current = hit.Accessible;
        for (var depth = 0; depth < maxDepth; depth++)
        {
            var parent = TryGetParent(current);
            if (parent is null)
                yield break;

            yield return new AccessibleHit(parent, ChildIdSelf);
            current = parent;
        }
    }

    public static IEnumerable<AccessibleHit> DescendantsWithin(IAccessible root, Rect bounds, int maxDepth = 12)
    {
        var budget = new TraversalBudget();
        foreach (var hit in DescendantsWithinCore(root, bounds, 0, maxDepth, budget, includeSelf: false))
            yield return hit;
    }

    public static bool HasDefaultAction(AccessibleHit hit)
        => !string.IsNullOrWhiteSpace(DefaultAction(hit));

    public static bool TryGetLocation(AccessibleHit hit, out Rect rect)
    {
        rect = Rect.Empty;
        try
        {
            hit.Accessible.accLocation(out var left, out var top, out var width, out var height, hit.ChildId);
            if (width <= 0 || height <= 0)
                return false;

            rect = new Rect(left, top, width, height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IAccessible? TryGetParent(IAccessible accessible)
    {
        try
        {
            return Ia2ComQuery.AsAccessible(accessible.accParent);
        }
        catch
        {
            return null;
        }
    }

    public static IAccessible? TryGetChild(IAccessible accessible, int childId)
    {
        try
        {
            return Ia2ComQuery.AsAccessible(accessible.get_accChild(childId));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<AccessibleHit> DescendantsWithinCore(
        IAccessible current,
        Rect bounds,
        int depth,
        int maxDepth,
        TraversalBudget budget,
        bool includeSelf)
    {
        if (budget.Visited++ > MaxTraversalNodes || depth > maxDepth)
            yield break;

        var self = new AccessibleHit(current, ChildIdSelf);
        if (includeSelf && Overlaps(bounds, self))
            yield return self;

        int count;
        try
        {
            count = current.accChildCount;
        }
        catch
        {
            yield break;
        }

        for (var childId = 1; childId <= count && budget.Visited <= MaxTraversalNodes; childId++)
        {
            var childHit = new AccessibleHit(current, childId);
            if (!Overlaps(bounds, childHit))
                continue;

            var child = TryGetChild(current, childId);
            if (child is null)
            {
                yield return childHit;
                continue;
            }

            foreach (var nested in DescendantsWithinCore(child, bounds, depth + 1, maxDepth, budget, includeSelf: true))
                yield return nested;
        }
    }

    private static bool Overlaps(Rect bounds, AccessibleHit hit)
    {
        if (!TryGetLocation(hit, out var rect))
            return true;

        rect.Intersect(bounds);
        return !rect.IsEmpty;
    }

    private static string? DefaultAction(AccessibleHit hit)
    {
        try
        {
            return hit.Accessible.get_accDefaultAction(hit.ChildId);
        }
        catch
        {
            return null;
        }
    }

    private static int? CoerceChildId(object value)
    {
        try
        {
            return value switch
            {
                int i => i,
                short s => s,
                long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
                _ => Convert.ToInt32(value, CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record AccessibleHit(IAccessible Accessible, object ChildId);
