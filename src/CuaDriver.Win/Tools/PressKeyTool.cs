using System.Text.Json.Nodes;
using System.Windows.Automation;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class PressKeyTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "press_key",
        ToolDescriptions.PressKey,
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND. Required when element_index is used.")),
            ("key", JsonArgs.Prop("string", "Key name, e.g. enter, escape, tab, a, f5.")),
            ("modifiers", JsonArgs.Prop("array", "Optional modifier names held while the key is pressed: ctrl, shift, alt/option, win/cmd.")),
            ("modifier", JsonArgs.Prop("array", "Alias for modifiers.")),
            ("element_index", JsonArgs.Prop("integer", "Optional element index from get_window_state. When present, the key targets that element's native HWND when available."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var key = JsonArgs.RequiredString(args, "key");
        var index = JsonArgs.OptionalInt(args, "element_index");
        var modifiers = JsonArgs.OptionalStringArray(args, "modifiers");
        if (modifiers.Length == 0)
            modifiers = JsonArgs.OptionalStringArray(args, "modifier");

        var windowId = JsonArgs.OptionalLong(args, "window_id");
        if (index is not null && windowId is null)
            return ToolResult.Error("window_id is required when element_index is used.");
        windowId ??= WindowEnumerator.MainWindowForPid(pid)?.WindowId;
        if (windowId is null)
            return ToolResult.Error($"No window found for pid {pid}.");

        var window = WindowEnumerator.Find(windowId.Value);
        if (window is null)
            return ToolResult.Error($"No window with window_id {windowId.Value}.");
        if (window.Pid != pid)
            return ToolResult.Error($"window_id {windowId.Value} belongs to pid {window.Pid}, not pid {pid}.");

        var targetHwnd = window.Hwnd;
        if (index is not null)
        {
            var element = context.State.UiaTree.GetCachedElement(pid, windowId.Value, index.Value);
            var elementHwnd = ElementHwnd(element);
            if (elementHwnd != IntPtr.Zero)
                targetHwnd = elementHwnd;
            context.State.LastUiaTextTarget[(pid, windowId.Value)] = element;
        }

        var receipt = await WindowMessageInput.PressKeyAsync(targetHwnd, key, modifiers, cancellationToken).ConfigureAwait(false);
        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }

    private static IntPtr ElementHwnd(AutomationElement element)
    {
        try
        {
            var hwnd = element.Current.NativeWindowHandle;
            return hwnd == 0 ? IntPtr.Zero : new IntPtr(hwnd);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
