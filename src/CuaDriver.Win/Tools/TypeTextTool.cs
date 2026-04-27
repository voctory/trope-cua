using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal sealed class TypeTextTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "type_text",
        ToolDescriptions.TypeText,
        JsonArgs.RequiredSchema(["pid", "text"],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("element_index", JsonArgs.Prop("integer", "Optional element index for UIA ValuePattern.")),
            ("text", JsonArgs.Prop("string", "Text to type or set.")),
            ("delay_ms", JsonArgs.Prop("integer", "Milliseconds between streamed text chunks, 0-200. Default 30.")),
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium debugging port.")),
            ("allow_transient_foreground", JsonArgs.Prop("boolean", "Allow a brief native/browser UIA foreground/focus blip when no verified background text route exists, then attempt to restore the previous foreground. Defaults true for existing-window actions; receipts still report background_safe=false when this happens."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
        => await InvokeAsync(args, context, streamCharacters: false, cancellationToken).ConfigureAwait(false);

    internal static async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, bool streamCharacters, CancellationToken cancellationToken)
    {
        var target = TypeTextArgs.Parse(args);

        ActionReceipt receipt;
        if (target.ElementIndex is not null)
        {
            if (target.WindowId is null)
                return ToolResult.Error("window_id is required for element_index type_text.");
            if (!ToolWindows.TryFindForPid(target.Pid, target.WindowId.Value, out var window, out var error))
                return error!;

            var element = context.State.UiaTree.GetCachedElement(target.Pid, target.WindowId.Value, target.ElementIndex.Value);
            await AgentCursorTooling.MoveToElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);

            var cdpPort = BrowserToolArgs.CdpPort(args, context);
            if (BrowserWindowClassifier.IsLikelyChromium(window) && cdpPort is null)
            {
                receipt = await TypeViaBrowserIa2Async(context, window.Hwnd, element, target.Text, target.DelayMs, streamCharacters, cancellationToken).ConfigureAwait(false);
                if (receipt.Ok)
                    return ActionToolResult.FromReceipt(receipt);

                if (target.AllowTransientForeground)
                {
                    receipt = await TypeViaElementAsync(context, window.Hwnd, element, target.Text, target.DelayMs, streamCharacters, allowTransientForeground: true, cancellationToken).ConfigureAwait(false);
                    return ActionToolResult.FromReceipt(receipt);
                }

                var refused = ActionReceipt.Failure(
                    "requires_cdp_or_child_session",
                    $"Chromium element text entry has no configured CDP route and the safe IA2 editable-text route failed ({receipt.Route}: {receipt.Reason}). If this is an existing user browser window, do not start a separate CDP/debugging browser; keep allow_transient_foreground enabled for the existing window, use the child-session/AppBroadcast lane, or report the blocker.");
                return ActionToolResult.FromReceipt(refused);
            }

            if (BrowserWindowClassifier.IsLikelyChromium(window) && cdpPort is not null)
            {
                receipt = await TypeViaBrowserCdpAsync(context, window, element, target.Text, target.DelayMs, cdpPort.Value, cancellationToken).ConfigureAwait(false);
                return ActionToolResult.FromReceipt(receipt);
            }

            receipt = await TypeViaElementAsync(context, window.Hwnd, element, target.Text, target.DelayMs, streamCharacters, target.AllowTransientForeground, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (!ToolWindows.TryFindMainOrForPid(target.Pid, target.WindowId, out var window, out var error))
                return error!;

            if (context.State.LastUiaTextTarget.TryGetValue((target.Pid, window.WindowId), out var textElement))
            {
                await AgentCursorTooling.MoveToElementAsync(context, textElement, window.Hwnd, cancellationToken).ConfigureAwait(false);
                var targetCdpPort = BrowserToolArgs.CdpPort(args, context);
                if (BrowserWindowClassifier.IsLikelyChromium(window) && targetCdpPort is not null)
                {
                    var browserReceipt = await TypeViaBrowserCdpAsync(context, window, textElement, target.Text, target.DelayMs, targetCdpPort.Value, cancellationToken).ConfigureAwait(false);
                    if (browserReceipt.Ok)
                        return ActionToolResult.FromReceipt(browserReceipt, "uia.last_text_target.");
                }

                var setReceipt = await TypeViaElementAsync(context, window.Hwnd, textElement, target.Text, target.DelayMs, streamCharacters, target.AllowTransientForeground, cancellationToken).ConfigureAwait(false);
                if (setReceipt.Ok)
                    return ActionToolResult.FromReceipt(setReceipt, "uia.last_text_target.");
            }

            var cdpPort = BrowserToolArgs.CdpPort(args, context);
            var cdpReceipt = await CdpBrowserBridge.TryTypeTextAsync(cdpPort, window.WindowId, target.Text, target.DelayMs, cancellationToken).ConfigureAwait(false);
            if (cdpReceipt is not null)
            {
                receipt = cdpReceipt;
            }
            else
            {
                if (BrowserWindowClassifier.IsLikelyBrowser(window))
                {
                    receipt = ActionReceipt.Failure("requires_cdp_or_uia_text_target", "Browser text input needs a prior UIA text-target click/element_index or a configured CDP port; refusing blind WM_CHAR.");
                }
                else
                {
                    var hwnd = context.State.LastTargetHwnd.TryGetValue((target.Pid, window.WindowId), out var clickedTarget)
                        ? clickedTarget
                        : WindowMessageTextTargeting.FindTextInputTarget(window.Hwnd);
                    receipt = await WindowMessageInput.TypeTextAsync(hwnd, target.Text, cancellationToken, target.DelayMs).ConfigureAwait(false);
                }
            }
        }

        return ActionToolResult.FromReceipt(receipt);
    }

    private static async Task<ActionReceipt> TypeViaBrowserCdpAsync(
        ToolContext context,
        WindowInfo window,
        System.Windows.Automation.AutomationElement element,
        string text,
        int delayMs,
        int cdpPort,
        CancellationToken cancellationToken)
    {
        var point = AgentCursorTooling.ElementCenter(element);
        if (point is null)
            return ActionReceipt.Failure("cdp.input.insert_text", "Element has no resolvable screen point for browser CDP focus.");

        var localX = point.Value.X - window.Bounds.X;
        var localY = point.Value.Y - window.Bounds.Y;
        var clickReceipt = await CdpBrowserBridge.TryClickAsync(window.Hwnd, window.WindowId, localX, localY, 1, rightButton: false, cdpPort, cancellationToken).ConfigureAwait(false)
                           ?? BrowserToolArgs.NoPageReceipt("cdp.input.dispatch_mouse", cdpPort);
        if (!clickReceipt.Ok)
            return clickReceipt;

        return await CdpBrowserBridge.TryTypeTextAsync(cdpPort, window.WindowId, text, delayMs, cancellationToken).ConfigureAwait(false)
               ?? BrowserToolArgs.NoPageReceipt("cdp.input.insert_text", cdpPort);
    }

    private static async Task<ActionReceipt> TypeViaBrowserIa2Async(
        ToolContext context,
        IntPtr rootHwnd,
        System.Windows.Automation.AutomationElement element,
        string text,
        int delayMs,
        bool streamCharacters,
        CancellationToken cancellationToken)
    {
        var units = TextElementSplitter.Split(text);
        if (delayMs <= 0 || units.Count <= 1)
        {
            var receipt = MsaaActions.InsertEditableTextAtElement(rootHwnd, element, text);
            if (receipt.Ok)
                return receipt with { Route = "browser.ia2." + receipt.Route };

            var replacement = MsaaActions.SetEditableTextAtElement(rootHwnd, element, text);
            return replacement with { Route = "browser.ia2.fallback_replace." + replacement.Route };
        }

        ActionReceipt? last = null;
        for (var i = 0; i < units.Count; i++)
        {
            var receipt = MsaaActions.InsertEditableTextAtElement(rootHwnd, element, units[i]);
            if (!receipt.Ok)
                return receipt with { Route = "browser.ia2." + receipt.Route };

            last = receipt;
            context.State.AgentCursor.KeepAlive();
            if (i + 1 < units.Count)
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return (last ?? ActionReceipt.Failure("ia2.editable_text.insert", "No text units to insert.")) with
        {
            Route = $"browser.ia2.{(last?.Route ?? "ia2.editable_text.insert")}.{(streamCharacters ? "chars" : "stream")}"
        };
    }

    private static async Task<ActionReceipt> TypeViaElementAsync(
        ToolContext context,
        IntPtr rootHwnd,
        System.Windows.Automation.AutomationElement element,
        string text,
        int delayMs,
        bool streamCharacters,
        bool allowTransientForeground,
        CancellationToken cancellationToken)
    {
        var units = TextElementSplitter.Split(text);
        if (delayMs <= 0 || units.Count <= 1)
            return InsertElementText(rootHwnd, element, text, allowTransientForeground);

        var insertReceipt = await StreamInsertionAsync(context, rootHwnd, element, units, delayMs, allowTransientForeground, cancellationToken).ConfigureAwait(false);
        if (insertReceipt.Ok)
            return insertReceipt with { Route = $"{insertReceipt.Route}.{(streamCharacters ? "chars" : "stream")}" };

        return await StreamReplacementAsync(context, rootHwnd, element, text, units, delayMs, allowTransientForeground, cancellationToken).ConfigureAwait(false);
    }

    private static ActionReceipt InsertElementText(IntPtr rootHwnd, System.Windows.Automation.AutomationElement element, string text, bool allowTransientForeground)
    {
        var receipt = MsaaActions.InsertEditableTextAtElement(rootHwnd, element, text);
        if (!receipt.Ok)
        {
            var replacement = SetElementText(rootHwnd, element, text, allowTransientForeground);
            receipt = replacement with { Route = "fallback_replace." + replacement.Route };
        }
        return receipt;
    }

    private static async Task<ActionReceipt> StreamInsertionAsync(
        ToolContext context,
        IntPtr rootHwnd,
        System.Windows.Automation.AutomationElement element,
        List<string> units,
        int delayMs,
        bool allowTransientForeground,
        CancellationToken cancellationToken)
    {
        ActionReceipt? last = null;
        for (var i = 0; i < units.Count; i++)
        {
            var receipt = MsaaActions.InsertEditableTextAtElement(rootHwnd, element, units[i]);
            if (!receipt.Ok)
                return receipt.ShouldStopFallback ? receipt : SetElementText(rootHwnd, element, string.Concat(units.Take(i + 1)), allowTransientForeground);

            last = receipt;
            context.State.AgentCursor.KeepAlive();
            if (i + 1 < units.Count)
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return last ?? ActionReceipt.Failure("ia2.editable_text.insert", "No text units to insert.");
    }

    private static async Task<ActionReceipt> StreamReplacementAsync(
        ToolContext context,
        IntPtr rootHwnd,
        System.Windows.Automation.AutomationElement element,
        string text,
        List<string> units,
        int delayMs,
        bool allowTransientForeground,
        CancellationToken cancellationToken)
    {
        ActionReceipt? last = null;
        var offset = 0;
        for (var i = 0; i < units.Count; i++)
        {
            offset += units[i].Length;
            var receipt = SetElementText(rootHwnd, element, text[..offset], allowTransientForeground);
            if (!receipt.Ok)
            {
                if (last is null)
                    return SetElementText(rootHwnd, element, text, allowTransientForeground);

                var final = SetElementText(rootHwnd, element, text, allowTransientForeground);
                return final.Ok
                    ? final with { Route = $"{last.Route}.stream.final_set" }
                    : receipt;
            }

            last = receipt;
            context.State.AgentCursor.KeepAlive();
            if (i + 1 < units.Count)
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return (last ?? SetElementText(rootHwnd, element, text, allowTransientForeground)) with
        {
            Route = $"{(last?.Route ?? "uia.value.set")}.stream_replace"
        };
    }

    private static ActionReceipt SetElementText(IntPtr rootHwnd, System.Windows.Automation.AutomationElement element, string text, bool allowTransientForeground)
    {
        var receipt = MsaaActions.SetEditableTextAtElement(rootHwnd, element, text);
        if (!receipt.Ok && !receipt.ShouldStopFallback)
        {
            if (RequiresIsolatedTextLane(element) && !allowTransientForeground)
                return ActionReceipt.Failure(
                    "requires_child_session",
                    "This text control did not expose a background-safe IA2/MSAA editable-text route, and UIA ValuePattern.SetValue can foreground native WinUI apps. Keep allow_transient_foreground enabled for the existing window, use the child-session/AppBroadcast lane, skip this optional text field, or report the blocker.");

            receipt = UiAutomationActions.SetValue(element, text, allowTransientForeground: allowTransientForeground);
        }
        return receipt;
    }

    private static bool RequiresIsolatedTextLane(System.Windows.Automation.AutomationElement element)
    {
        try
        {
            var current = element.Current;
            if (current.NativeWindowHandle != 0)
                return false;

            var localizedType = current.LocalizedControlType ?? "";
            var className = current.ClassName ?? "";
            return localizedType.Contains("edit", StringComparison.OrdinalIgnoreCase)
                   || className.Contains("TextBox", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private sealed record TypeTextArgs(int Pid, long? WindowId, int? ElementIndex, string Text, int DelayMs, bool AllowTransientForeground)
    {
        public static TypeTextArgs Parse(JsonObject args) => new(
            JsonArgs.RequiredInt(args, "pid"),
            JsonArgs.OptionalLong(args, "window_id"),
            JsonArgs.OptionalInt(args, "element_index"),
            JsonArgs.RequiredString(args, "text"),
            Math.Clamp(JsonArgs.OptionalInt(args, "delay_ms") ?? 30, 0, 200),
            JsonArgs.OptionalBool(args, "allow_transient_foreground", true));
    }
}
