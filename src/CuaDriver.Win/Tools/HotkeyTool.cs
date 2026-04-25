using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class HotkeyTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "hotkey",
        ToolDescriptions.Hotkey,
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("key", JsonArgs.Prop("string", "Main key.")),
            ("modifiers", JsonArgs.Prop("array", "Modifiers: ctrl, shift, alt, win/cmd."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var key = JsonArgs.RequiredString(args, "key");
        var modifiers = JsonArgs.OptionalStringArray(args, "modifiers");
        if (modifiers.Length == 0)
            modifiers = JsonArgs.OptionalStringArray(args, "modifier");

        var windowId = JsonArgs.OptionalLong(args, "window_id") ?? WindowEnumerator.MainWindowForPid(pid)?.WindowId;
        if (windowId is null)
            return ToolResult.Error($"No window found for pid {pid}.");

        var receipt = await WindowMessageInput.PressKeyAsync(new IntPtr(windowId.Value), key, modifiers, cancellationToken).ConfigureAwait(false);
        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }
}
