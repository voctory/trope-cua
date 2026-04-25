using System.Globalization;
using System.Text.Json.Nodes;
using CuaDriver.Win.Browser;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class TypeTextTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "type_text",
        ToolDescriptions.TypeText,
        JsonArgs.Schema(
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
        => await InvokeAsync(args, context, cancellationToken, streamCharacters: false).ConfigureAwait(false);

    internal async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken, bool streamCharacters)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var text = JsonArgs.RequiredString(args, "text");
        var windowId = JsonArgs.OptionalLong(args, "window_id");
        var index = JsonArgs.OptionalInt(args, "element_index");
        var delayMs = Math.Clamp(JsonArgs.OptionalInt(args, "delay_ms") ?? 30, 0, 200);

        ActionReceipt receipt;
        if (index is not null)
        {
            if (windowId is null)
                return ToolResult.Error("window_id is required for element_index type_text.");
            var window = WindowEnumerator.Find(windowId.Value);
            if (window is null)
                return ToolResult.Error($"No window with window_id {windowId.Value}.");
            if (window.Pid != pid)
                return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");

            var cdpPort = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
            if (BrowserWindowClassifier.IsLikelyChromium(window) && cdpPort is null)
            {
                var refused = ActionReceipt.Failure(
                    "requires_cdp_or_child_session",
                    "Refusing Chromium UIA/IA2 text setters from the parent session because Chromium can foreground the target while focusing editable web content. Provide cdp_port, configure chromium_debugging_port, or use the child-session/AppBroadcast lane.");
                return ToolResult.Text("❌ " + refused.ToJson(), true);
            }

            var element = context.State.UiaTree.GetCachedElement(pid, windowId.Value, index.Value);
            await AgentCursorTooling.MoveToElementAsync(context, element, window.Hwnd, cancellationToken).ConfigureAwait(false);
            receipt = await TypeViaElementAsync(context, window.Hwnd, element, text, delayMs, streamCharacters, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (windowId is null)
            {
                var w = WindowEnumerator.MainWindowForPid(pid);
                if (w is null)
                    return ToolResult.Error($"No window found for pid {pid}.");
                windowId = w.WindowId;
            }

            var window = WindowEnumerator.Find(windowId.Value);
            if (window is null)
                return ToolResult.Error($"No window with window_id {windowId.Value}.");
            if (window.Pid != pid)
                return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");

            if (context.State.LastUiaTextTarget.TryGetValue((pid, windowId.Value), out var textElement))
            {
                await AgentCursorTooling.MoveToElementAsync(context, textElement, window.Hwnd, cancellationToken).ConfigureAwait(false);
                var setReceipt = await TypeViaElementAsync(context, window.Hwnd, textElement, text, delayMs, streamCharacters, cancellationToken).ConfigureAwait(false);
                if (setReceipt.Ok)
                    return ToolResult.Text("✅ " + (setReceipt with { Route = "uia.last_text_target." + setReceipt.Route }).ToJson());
            }

            var cdpPort = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
            var cdp = new CdpBrowserBridge(context.State.UiaTree);
            var cdpReceipt = await cdp.TryTypeTextAsync(cdpPort, text, delayMs, cancellationToken).ConfigureAwait(false);
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
                    var target = context.State.LastTargetHwnd.TryGetValue((pid, windowId.Value), out var clickedTarget)
                        ? clickedTarget
                        : WindowMessageInput.FindTextInputTarget(window.Hwnd);
                    receipt = await WindowMessageInput.TypeTextAsync(target, text, cancellationToken, delayMs).ConfigureAwait(false);
                }
            }
        }

        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
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
        var units = TextElements(text);
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
        IReadOnlyList<string> units,
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
        IReadOnlyList<string> units,
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

    private static IReadOnlyList<string> TextElements(string text)
    {
        var result = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            result.Add(enumerator.GetTextElement());
        return result;
    }
}
