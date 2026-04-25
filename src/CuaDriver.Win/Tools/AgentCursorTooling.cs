using System.Windows.Automation;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal static class AgentCursorTooling
{
    public static POINT? ElementCenter(AutomationElement element)
    {
        try
        {
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty)
                return null;

            return new POINT(
                (int)Math.Round(rect.X + rect.Width / 2),
                (int)Math.Round(rect.Y + rect.Height / 2));
        }
        catch
        {
            return null;
        }
    }

    public static async Task MoveToElementAsync(ToolContext context, AutomationElement element, CancellationToken ct)
    {
        var center = ElementCenter(element);
        if (center is { } point)
            await context.State.AgentCursor.MoveToAsync(point, ct).ConfigureAwait(false);
    }

    public static async Task PulseAtElementAsync(ToolContext context, AutomationElement element, CancellationToken ct)
    {
        var center = ElementCenter(element);
        if (center is { } point)
            await context.State.AgentCursor.ClickPulseAsync(point, ct).ConfigureAwait(false);
    }
}
