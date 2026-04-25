using System.Globalization;
using Accessibility;

namespace CuaDriver.Win.Uia;

internal static class MsaaAccessibleTree
{
    private const int ChildIdSelf = 0;

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

    private static IAccessible? TryGetChild(IAccessible accessible, int childId)
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
