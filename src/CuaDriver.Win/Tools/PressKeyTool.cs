using System.Text.Json.Nodes;
using System.Windows.Automation;
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
            ("element_index", JsonArgs.Prop("integer", "Optional element index from get_window_state. When present, the key targets that element's native HWND when available."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var key = JsonArgs.RequiredString(args, "key");
        var index = JsonArgs.OptionalInt(args, "element_index");
        var modifiers = JsonArgs.OptionalStringArray(args, "modifiers", "modifier");

        var windowId = JsonArgs.OptionalLong(args, "window_id");
        if (index is not null && windowId is null)
            return ToolResult.Error("window_id is required when element_index is used.");
        if (!ToolWindows.TryFindMainOrForPid(pid, windowId, out var window, out var error))
            return error!;

        var targetHwnd = window.Hwnd;
        if (index is not null)
        {
            var element = context.State.UiaTree.GetCachedElement(pid, window.WindowId, index.Value);
            var elementHwnd = ElementHwnd(element);
            if (elementHwnd != IntPtr.Zero)
                targetHwnd = elementHwnd;
            context.State.LastUiaTextTarget[(pid, window.WindowId)] = element;
        }

        var receipt = await WindowMessageInput.PressKeyAsync(targetHwnd, key, modifiers, cancellationToken).ConfigureAwait(false);
        return ActionToolResult.FromReceipt(receipt);
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
