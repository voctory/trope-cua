using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Input;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal static class ActionToolResult
{
    public static ToolResult FromReceipt(ActionReceipt receipt)
    {
        return ToolResult.Text((receipt.Ok ? "✅ " : "❌ ") + receipt.ToJson(), StructuredReceipt(receipt), !receipt.Ok);
    }

    public static ToolResult FromReceipt(ActionReceipt receipt, string routePrefix) =>
        FromReceipt(receipt with { Route = routePrefix + receipt.Route });

    public static JsonObject StructuredReceipt(ActionReceipt receipt) =>
        JsonSerializer.SerializeToNode(receipt, JsonUtil.SerializerOptions)?.AsObject()
        ?? new JsonObject();
}
