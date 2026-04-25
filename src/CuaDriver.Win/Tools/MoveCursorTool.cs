using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class MoveCursorTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "move_cursor",
        "Move the visual agent cursor to a screen point. This never moves the parent-session cursor unless allow_parent_cursor=true.",
        JsonArgs.Schema(
            ("x", JsonArgs.Prop("integer", "Screen X.")),
            ("y", JsonArgs.Prop("integer", "Screen Y.")),
            ("window_id", JsonArgs.Prop("integer", "Optional target HWND used to layer the visual cursor just above that window.")),
            ("allow_parent_cursor", JsonArgs.Prop("boolean", "Explicit unsafe override for moving the real parent-session cursor."))),
        Destructive: true,
        Idempotent: false);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var x = JsonArgs.RequiredInt(args, "x");
        var y = JsonArgs.RequiredInt(args, "y");
        var windowId = JsonArgs.OptionalLong(args, "window_id");
        var allow = JsonArgs.OptionalBool(args, "allow_parent_cursor");
        var point = new POINT(x, y);
        IntPtr? targetHwnd = null;
        if (windowId is not null)
        {
            var window = WindowEnumerator.Find(windowId.Value);
            if (window is null)
                return ToolResult.Error($"No window with window_id {windowId.Value}.");
            targetHwnd = window.Hwnd;
        }

        if (!allow)
        {
            await context.State.AgentCursor.MoveToAsync(point, targetHwnd, cancellationToken).ConfigureAwait(false);
            return ToolResult.Text("✅ " + ActionReceipt.Success("agent_cursor.visual_move").ToJson());
        }

        var guard = NoRegressionGuard.Capture();
        NativeMethods.SetCursorPos(x, y);
        await context.State.AgentCursor.MoveToAsync(point, targetHwnd, cancellationToken).ConfigureAwait(false);
        var done = guard.Finish(ActionReceipt.Success("parent.setcursorpos"), allowCursorMove: true);
        return ToolResult.Text("✅ " + done.ToJson());
    }
}
