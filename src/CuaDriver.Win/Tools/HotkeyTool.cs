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
        JsonArgs.SchemaWithAnyOf(["pid"], [["keys"], ["key"]],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("keys", JsonArgs.Prop("array", "Modifier(s) and one non-modifier key, e.g. [\"ctrl\", \"c\"]. Mac-compatible shape.")),
            ("key", JsonArgs.Prop("string", "Main key. Windows-compatible alias used with modifiers.")),
            ("modifiers", JsonArgs.Prop("array", "Modifiers: ctrl, shift, alt, win/cmd."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var keys = JsonArgs.OptionalStringArray(args, "keys");
        string key;
        string[] modifiers;
        if (keys.Length > 0)
        {
            if (keys.Length < 2)
                return ToolResult.Error("keys must include at least one modifier and one non-modifier key.");
            key = keys.Last();
            modifiers = keys.Take(keys.Length - 1).ToArray();
        }
        else
        {
            key = JsonArgs.RequiredString(args, "key");
            modifiers = JsonArgs.OptionalStringArray(args, "modifiers");
            if (modifiers.Length == 0)
                modifiers = JsonArgs.OptionalStringArray(args, "modifier");
        }

        var windowId = JsonArgs.OptionalLong(args, "window_id") ?? WindowEnumerator.MainWindowForPid(pid)?.WindowId;
        if (windowId is null)
            return ToolResult.Error($"No window found for pid {pid}.");

        var window = WindowEnumerator.Find(windowId.Value);
        if (window is null)
            return ToolResult.Error($"No window with window_id {windowId.Value}.");
        if (window.Pid != pid)
            return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");

        var receipt = await WindowMessageInput.PressKeyAsync(window.Hwnd, key, modifiers, cancellationToken).ConfigureAwait(false);
        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }
}
