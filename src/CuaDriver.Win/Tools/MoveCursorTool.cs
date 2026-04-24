using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class MoveCursorTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "move_cursor",
        "Compatibility tool. Refuses to move the parent-session cursor unless allow_parent_cursor=true.",
        JsonArgs.Schema(
            ("x", JsonArgs.Prop("integer", "Screen X.")),
            ("y", JsonArgs.Prop("integer", "Screen Y.")),
            ("allow_parent_cursor", JsonArgs.Prop("boolean", "Explicit unsafe override."))),
        Destructive: true,
        Idempotent: false);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var x = JsonArgs.RequiredInt(args, "x");
        var y = JsonArgs.RequiredInt(args, "y");
        var allow = JsonArgs.OptionalBool(args, "allow_parent_cursor");
        if (!allow)
        {
            var receipt = ActionReceipt.Failure("requires_child_session_or_appbroadcast", "Refusing to move the parent-session cursor. Use the child-session lane or pass allow_parent_cursor=true for a local experiment.");
            return Task.FromResult(ToolResult.Text("❌ " + receipt.ToJson(), true));
        }

        var guard = NoRegressionGuard.Capture();
        NativeMethods.SetCursorPos(x, y);
        var done = guard.Finish(ActionReceipt.Success("parent.setcursorpos"), allowCursorMove: true);
        return Task.FromResult(ToolResult.Text("✅ " + done.ToJson()));
    }
}
