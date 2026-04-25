using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Uia;

namespace CuaDriver.Win.Tools;

public sealed class SetValueTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "set_value",
        ToolDescriptions.SetValue,
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("element_index", JsonArgs.Prop("integer", "Element index from get_window_state.")),
            ("value", JsonArgs.Prop("string", "String value, or numeric text for range controls."))),
        Destructive: true,
        Idempotent: true);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.RequiredLong(args, "window_id");
        var index = JsonArgs.RequiredInt(args, "element_index");
        var value = JsonArgs.RequiredString(args, "value");

        var element = context.State.UiaTree.GetCachedElement(pid, windowId, index);
        await AgentCursorTooling.MoveToElementAsync(context, element, cancellationToken).ConfigureAwait(false);

        ActionReceipt receipt;
        if (double.TryParse(value, out var number))
        {
            receipt = UiAutomationActions.SetRangeValue(element, number);
            if (!receipt.Ok)
                receipt = MsaaActions.SetEditableTextAtElement(new IntPtr(windowId), element, value);
            if (!receipt.Ok)
                receipt = UiAutomationActions.SetValue(element, value);
        }
        else
        {
            receipt = MsaaActions.SetEditableTextAtElement(new IntPtr(windowId), element, value);
            if (!receipt.Ok)
                receipt = UiAutomationActions.SetValue(element, value);
        }

        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), !receipt.Ok);
    }
}
