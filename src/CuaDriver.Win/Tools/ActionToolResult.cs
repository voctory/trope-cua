using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal static class ActionToolResult
{
    public static ToolResult FromReceipt(ActionReceipt receipt)
    {
        var structured = receipt.ToJsonObject();
        return ToolResult.Text(ReceiptText(receipt), structured, !receipt.Ok);
    }

    public static ToolResult FromReceipt(ActionReceipt receipt, string routePrefix) =>
        FromReceipt(receipt with { Route = routePrefix + receipt.Route });

    public static JsonObject StructuredReceipt(ActionReceipt receipt) => receipt.ToJsonObject();

    private static string ReceiptText(ActionReceipt receipt)
    {
        var prefix = receipt.Ok ? ToolText.OkPrefix : ToolText.ErrorPrefix;
        var text = $"{prefix}route={receipt.Route}";
        if (!receipt.BackgroundSafe)
            text += " background_safe=false";
        if (receipt.CursorMoved)
            text += " cursor_moved=true";
        if (receipt.ForegroundChanged)
            text += " foreground_changed=true";
        return string.IsNullOrWhiteSpace(receipt.Reason) ? text : text + "\nreason: " + receipt.Reason;
    }
}
