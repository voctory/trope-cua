using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal sealed class MoveCursorTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "move_cursor",
        "Move the visual agent cursor overlay to a screen point. This is for displaying agent intent only and should not be used as an input route. It never moves the parent-session cursor unless allow_parent_cursor=true, which is an unsafe local experiment requiring explicit user intent.",
        JsonArgs.RequiredSchema(["x", "y"],
            ("x", JsonArgs.Prop("integer", "Screen X.")),
            ("y", JsonArgs.Prop("integer", "Screen Y.")),
            ("window_id", JsonArgs.Prop("integer", "Optional target HWND used to layer the visual cursor just above that window.")),
            ("allow_parent_cursor", JsonArgs.Prop("boolean", "Explicit unsafe override for moving the real parent-session cursor. Do not set for background automation."))),
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
            if (!ToolWindows.TryFind(windowId.Value, out var window, out var error))
                return error!;
            targetHwnd = window.Hwnd;
        }

        if (!allow)
        {
            await context.State.AgentCursor.MoveToAsync(point, targetHwnd, cancellationToken).ConfigureAwait(false);
            return ActionToolResult.FromReceipt(ActionReceipt.Success("agent_cursor.visual_move"));
        }

        using var guard = NoRegressionGuard.Capture();
        var movedRealCursor = NativeMethods.SetCursorPos(x, y);
        await context.State.AgentCursor.MoveToAsync(point, targetHwnd, cancellationToken).ConfigureAwait(false);
        var receipt = movedRealCursor
            ? ActionReceipt.UnsafeSuccess("parent.setcursorpos")
            : ActionReceipt.Failure("parent.setcursorpos", "SetCursorPos failed.");
        var done = guard.Finish(receipt, allowCursorMove: true, allowUnsafeRoute: true);
        return ActionToolResult.FromReceipt(done);
    }
}
