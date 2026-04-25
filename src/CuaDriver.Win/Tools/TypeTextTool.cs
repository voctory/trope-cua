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
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium debugging port."))),
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

            var cdpPort = BrowserToolArgs.CdpPort(args, context);
            if (BrowserWindowClassifier.IsLikelyChromium(window) && cdpPort is null)
            {
                var refused = ActionReceipt.Failure(
                    "requires_cdp_or_child_session",
                    "Refusing Chromium UIA/IA2 text setters from the parent session because Chromium can foreground the target while focusing editable web content. Provide cdp_port, configure chromium_debugging_port, or use the child-session/AppBroadcast lane.");
                return ActionToolResult.FromReceipt(refused);
            }

            var element = context.State.UiaTree.GetCachedElement(target.Pid, target.WindowId.Value, target.ElementIndex.Value);
            await AgentCursorTooling.MoveToElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
            if (BrowserWindowClassifier.IsLikelyChromium(window) && cdpPort is not null)
            {
                receipt = await TypeViaBrowserCdpAsync(context, window, element, target.Text, target.DelayMs, cdpPort.Value, cancellationToken).ConfigureAwait(false);
                return ActionToolResult.FromReceipt(receipt);
            }

            receipt = await TypeViaElementAsync(context, window.Hwnd, element, target.Text, target.DelayMs, streamCharacters, cancellationToken).ConfigureAwait(false);
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

                var setReceipt = await TypeViaElementAsync(context, window.Hwnd, textElement, target.Text, target.DelayMs, streamCharacters, cancellationToken).ConfigureAwait(false);
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

    private static async Task<ActionReceipt> TypeViaElementAsync(
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
            return InsertElementText(rootHwnd, element, text);

        var insertReceipt = await StreamInsertionAsync(context, rootHwnd, element, units, delayMs, cancellationToken).ConfigureAwait(false);
        if (insertReceipt.Ok)
            return insertReceipt with { Route = $"{insertReceipt.Route}.{(streamCharacters ? "chars" : "stream")}" };

        return await StreamReplacementAsync(context, rootHwnd, element, text, units, delayMs, cancellationToken).ConfigureAwait(false);
    }

    private static ActionReceipt InsertElementText(IntPtr rootHwnd, System.Windows.Automation.AutomationElement element, string text)
    {
        var receipt = MsaaActions.InsertEditableTextAtElement(rootHwnd, element, text);
        if (!receipt.Ok)
        {
            var replacement = SetElementText(rootHwnd, element, text);
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
        CancellationToken cancellationToken)
    {
        ActionReceipt? last = null;
        for (var i = 0; i < units.Count; i++)
        {
            var receipt = MsaaActions.InsertEditableTextAtElement(rootHwnd, element, units[i]);
            if (!receipt.Ok)
                return receipt;

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
        CancellationToken cancellationToken)
    {
        ActionReceipt? last = null;
        var offset = 0;
        for (var i = 0; i < units.Count; i++)
        {
            offset += units[i].Length;
            var receipt = SetElementText(rootHwnd, element, text[..offset]);
            if (!receipt.Ok)
            {
                if (last is null)
                    return SetElementText(rootHwnd, element, text);

                var final = SetElementText(rootHwnd, element, text);
                return final.Ok
                    ? final with { Route = $"{last.Route}.stream.final_set" }
                    : receipt;
            }

            last = receipt;
            context.State.AgentCursor.KeepAlive();
            if (i + 1 < units.Count)
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return (last ?? SetElementText(rootHwnd, element, text)) with
        {
            Route = $"{(last?.Route ?? "uia.value.set")}.stream_replace"
        };
    }

    private static ActionReceipt SetElementText(IntPtr rootHwnd, System.Windows.Automation.AutomationElement element, string text)
    {
        var receipt = MsaaActions.SetEditableTextAtElement(rootHwnd, element, text);
        if (!receipt.Ok)
            receipt = UiAutomationActions.SetValue(element, text);
        return receipt;
    }

    private sealed record TypeTextArgs(int Pid, long? WindowId, int? ElementIndex, string Text, int DelayMs)
    {
        public static TypeTextArgs Parse(JsonObject args) => new(
            JsonArgs.RequiredInt(args, "pid"),
            JsonArgs.OptionalLong(args, "window_id"),
            JsonArgs.OptionalInt(args, "element_index"),
            JsonArgs.RequiredString(args, "text"),
            Math.Clamp(JsonArgs.OptionalInt(args, "delay_ms") ?? 30, 0, 200));
    }
}
