using System.Text.Json.Nodes;
using System.Windows.Automation;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal sealed class PressKeyTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "press_key",
        ToolDescriptions.PressKey,
        JsonArgs.RequiredSchema(["pid", "key"],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND. Required when element_index is used.")),
            ("key", JsonArgs.Prop("string", "Key name, e.g. enter, escape, tab, a, f5.")),
            ("modifiers", JsonArgs.Prop("array", "Optional modifier names held while the key is pressed: ctrl, shift, alt/option, win/cmd.")),
            ("modifier", JsonArgs.Prop("array", "Alias for modifiers.")),
            ("element_index", JsonArgs.Prop("integer", "Optional element index from get_window_state. When present, the key targets that element's native HWND when available.")),
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium debugging port for browser key events.")),
            ("allow_transient_foreground", JsonArgs.Prop("boolean", "Allow a brief foreground/focus blip for browser/native surfaces that ignore posted background key messages, then attempt to restore foreground. Defaults true for existing-window actions; receipts still report background_safe=false when this happens."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var key = JsonArgs.RequiredString(args, "key");
        var index = JsonArgs.OptionalInt(args, "element_index");
        var modifiers = JsonArgs.OptionalStringArray(args, "modifiers", "modifier");
        var allowTransientForeground = JsonArgs.OptionalBool(args, "allow_transient_foreground", true);

        var windowId = JsonArgs.OptionalLong(args, "window_id");
        if (index is not null && windowId is null)
            return ToolResult.Error("window_id is required when element_index is used.");
        if (!ToolWindows.TryFindMainOrForPid(pid, windowId, out var window, out var error))
            return error!;

        var targetHwnd = window.Hwnd;
        var isBrowser = BrowserWindowClassifier.IsLikelyBrowser(window);
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
            var cdpPort = BrowserToolArgs.CdpPort(args, context);
            var cdpReceipt = await CdpBrowserBridge.TryPressKeyAsync(cdpPort, window.WindowId, key, modifiers, cancellationToken).ConfigureAwait(false);
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

            if (!allowTransientForeground)
            {
                var refused = ActionReceipt.Failure(
                    "requires_cdp_or_transient_foreground",
                    "Browser key input is not reliably delivered by background HWND messages. If this is an existing user browser window, do not start a separate CDP/debugging browser; keep allow_transient_foreground enabled for the existing window, use the child-session/AppBroadcast lane, or report the blocker.");
                return ActionToolResult.FromReceipt(refused);
            }

            var foregroundReceipt = await WindowMessageInput.PressKeyWithTransientForegroundAsync(targetHwnd, key, modifiers, cancellationToken).ConfigureAwait(false);
            return ActionToolResult.FromReceipt(foregroundReceipt);
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
}
