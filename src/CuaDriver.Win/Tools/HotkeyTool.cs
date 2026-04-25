using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal sealed class HotkeyTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "hotkey",
        ToolDescriptions.Hotkey,
        JsonArgs.SchemaWithAnyOf(["pid"], [["keys"], ["key"]],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("keys", JsonArgs.Prop("array", "Modifier(s) and one non-modifier key, e.g. [\"ctrl\", \"c\"]. Preferred shape.")),
            ("key", JsonArgs.Prop("string", "Main key. Windows-compatible alias used with modifiers.")),
            ("modifiers", JsonArgs.Prop("array", "Modifiers: ctrl, shift, alt, win/cmd.")),
            ("modifier", JsonArgs.Prop("array", "Alias for modifiers."))),
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
            modifiers = JsonArgs.OptionalStringArray(args, "modifiers", "modifier");
        }

        if (!ToolWindows.TryFindMainOrForPid(pid, JsonArgs.OptionalLong(args, "window_id"), out var window, out var error))
            return error!;

        var receipt = await WindowMessageInput.PressKeyAsync(window.Hwnd, key, modifiers, cancellationToken).ConfigureAwait(false);
        return ActionToolResult.FromReceipt(receipt);
    }
}
