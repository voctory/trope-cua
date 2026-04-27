using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
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
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium debugging port for browser key events.")),
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
        var parseError = TryParseKeys(args, out var key, out var modifiers);
        if (parseError is not null)
            return parseError;

        if (!ToolWindows.TryFindMainOrForPid(pid, JsonArgs.OptionalLong(args, "window_id"), out var window, out var error))
            return error!;

        if (BrowserWindowClassifier.IsLikelyBrowser(window))
        {
            var cdpReceipt = await CdpBrowserBridge.TryPressKeyAsync(BrowserToolArgs.CdpPort(args, context), window.WindowId, key, modifiers, cancellationToken).ConfigureAwait(false);
            if (cdpReceipt is not null)
                return ActionToolResult.FromReceipt(cdpReceipt);

            return ActionToolResult.FromReceipt(ActionReceipt.Failure(
                "requires_cdp",
                "Browser hotkeys are not reliably delivered by background HWND messages. Provide cdp_port or configure chromium_debugging_port."));
        }

        var receipt = await WindowMessageInput.PressKeyAsync(window.Hwnd, key, modifiers, cancellationToken).ConfigureAwait(false);
        return ActionToolResult.FromReceipt(receipt);
    }

    private static ToolResult? TryParseKeys(JsonObject args, out string key, out string[] modifiers)
    {
        var keys = JsonArgs.OptionalStringArray(args, "keys");
        if (keys.Length > 0)
        {
            key = keys.LastOrDefault() ?? "";
            modifiers = keys.Take(Math.Max(0, keys.Length - 1)).ToArray();
            return keys.Length < 2
                ? ToolResult.Error("keys must include at least one modifier and one non-modifier key.")
                : null;
        }

        key = JsonArgs.RequiredString(args, "key");
        modifiers = JsonArgs.OptionalStringArray(args, "modifiers", "modifier");
        return null;
    }
}
