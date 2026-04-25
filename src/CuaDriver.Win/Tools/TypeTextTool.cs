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
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium debugging port."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var text = JsonArgs.RequiredString(args, "text");
        var windowId = JsonArgs.OptionalLong(args, "window_id");
        var index = JsonArgs.OptionalInt(args, "element_index");
        var delayMs = Math.Clamp(JsonArgs.OptionalInt(args, "delay_ms") ?? 4, 0, 200);

        ActionReceipt receipt;
        if (index is not null)
        {
            if (windowId is null)
                return ToolResult.Error("window_id is required for element_index type_text.");
            var element = context.State.UiaTree.GetCachedElement(pid, windowId.Value, index.Value);
            receipt = UiAutomationActions.SetValue(element, text);
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
                var setReceipt = UiAutomationActions.SetValue(textElement, text);
                if (setReceipt.Ok)
                    return ToolResult.Text("✅ " + (setReceipt with { Route = "uia.last_text_target." + setReceipt.Route }).ToJson());
            }

            var cdpPort = JsonArgs.OptionalInt(args, "cdp_port") ?? context.State.Config.ChromiumDebuggingPort;
            var cdp = new CdpBrowserBridge(context.State.UiaTree);
            var cdpReceipt = await cdp.TryTypeTextAsync(cdpPort, text, cancellationToken).ConfigureAwait(false);
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
}
