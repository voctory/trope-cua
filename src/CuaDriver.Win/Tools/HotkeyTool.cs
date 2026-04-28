using System.Text.Json.Nodes;
using System.Windows.Automation;
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
            ("window_id", JsonArgs.Prop("integer", "Target HWND. Required when element_index is used.")),
            ("element_index", JsonArgs.Prop("integer", "Optional get_window_state element_index; targets native HWND when available.")),
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

        var index = JsonArgs.OptionalInt(args, "element_index");
        var windowId = JsonArgs.OptionalLong(args, "window_id");
        if (index is not null && windowId is null)
            return ToolResult.Error("window_id is required when element_index is used.");
        if (!ToolWindows.TryFindMainOrForPid(pid, windowId, out var window, out var error))
            return error!;

        var isBrowser = BrowserWindowClassifier.IsLikelyBrowser(window);
        var targetHwnd = window.Hwnd;
        AutomationElement? element = null;
        if (index is not null)
        {
            element = context.State.UiaTree.GetCachedElement(pid, window.WindowId, index.Value);
            var elementHwnd = ToolWindows.NativeHwndForElement(element);
            if (elementHwnd != IntPtr.Zero && !isBrowser)
                targetHwnd = elementHwnd;
            context.State.LastUiaTextTarget[(pid, window.WindowId)] = element;
        }

        if (isBrowser)
        {
            var cdpReceipt = await CdpBrowserBridge.TryPressKeyAsync(BrowserToolArgs.CdpPort(args, context), window.WindowId, key, modifiers, cancellationToken).ConfigureAwait(false);
            if (cdpReceipt is not null)
                return ActionToolResult.FromReceipt(cdpReceipt);

            if (element is null)
                context.State.LastUiaTextTarget.TryGetValue((pid, window.WindowId), out element);

            if (element is not null)
            {
                var backgroundReceipt = await BrowserBackgroundKeyboard.PressKeyAsync(window, element, key, modifiers, cancellationToken).ConfigureAwait(false);
                if (backgroundReceipt.Ok || backgroundReceipt.ShouldStopFallback)
                    return ActionToolResult.FromReceipt(backgroundReceipt);
            }

            return ActionToolResult.FromReceipt(ActionReceipt.Failure(
                "requires_cdp_or_uia_text_target",
                "Browser hotkeys need CDP or a prior browser text target so the driver can establish internal browser focus with a background HWND click."));
        }

        if (index is null
            && context.State.LastTargetHwnd.TryGetValue((pid, window.WindowId), out var clickedTarget)
            && clickedTarget != IntPtr.Zero)
        {
            targetHwnd = clickedTarget;
        }

        var receipt = await WindowMessageInput.PressKeyAsync(targetHwnd, key, modifiers, cancellationToken).ConfigureAwait(false);
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
