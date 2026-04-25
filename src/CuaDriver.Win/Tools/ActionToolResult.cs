using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal static class ActionToolResult
{
    public static ToolResult FromReceipt(ActionReceipt receipt)
    {
        var structured = receipt.ToJsonObject();
        return ToolResult.JsonText(receipt.Ok ? "✅ " : "❌ ", structured, !receipt.Ok);
    }

    public static ToolResult FromReceipt(ActionReceipt receipt, string routePrefix) =>
        FromReceipt(receipt with { Route = routePrefix + receipt.Route });

    public static JsonObject StructuredReceipt(ActionReceipt receipt) => receipt.ToJsonObject();
}
