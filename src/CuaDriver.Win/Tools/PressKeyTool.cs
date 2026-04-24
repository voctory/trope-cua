using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class PressKeyTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "press_key",
        "Post a key to the target HWND without parent-session SendInput.",
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("key", JsonArgs.Prop("string", "Key name, e.g. enter, escape, tab, a, f5."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var key = JsonArgs.RequiredString(args, "key");
        var windowId = JsonArgs.OptionalLong(args, "window_id") ?? WindowEnumerator.MainWindowForPid(pid)?.WindowId;
        if (windowId is null)
            return ToolResult.Error($"No window found for pid {pid}.");

        var receipt = await WindowMessageInput.PressKeyAsync(new IntPtr(windowId.Value), key, [], cancellationToken).ConfigureAwait(false);
        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }
}
