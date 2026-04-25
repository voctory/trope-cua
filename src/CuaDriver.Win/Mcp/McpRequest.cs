using System.Text.Json;
using System.Text.Json.Nodes;

namespace CuaDriver.Win.Mcp;

internal sealed record McpRequest(JsonObject Raw, JsonNode? Id, string Method)
{
    public bool IsNotification => Method.StartsWith("notifications/", StringComparison.Ordinal);

    public static McpRequest Parse(string line)
    {
        var raw = JsonNode.Parse(line) as JsonObject
                  ?? throw new McpRequestException(-32600, "Invalid request: expected a JSON object.");
        var id = raw["id"]?.DeepClone();
        var method = OptionalString(raw, "method", -32600, "Invalid request: method must be a string.") ?? "";
        if (string.IsNullOrWhiteSpace(method))
            throw new McpRequestException(-32600, "Invalid request: missing method.");

        return new McpRequest(raw, id, method);
    }

    public ToolCallRequest ToolCall()
    {
        var p = Raw["params"] is null
            ? new JsonObject()
            : Raw["params"] as JsonObject ?? throw new McpRequestException(-32602, "tools/call params must be a JSON object");
        var name = OptionalString(p, "name", -32602, "tools/call params.name must be a string")
                   ?? throw new McpRequestException(-32602, "tools/call missing params.name");
        var args = p["arguments"] is null
            ? new JsonObject()
            : p["arguments"] as JsonObject ?? throw new McpRequestException(-32602, "tools/call params.arguments must be a JSON object");

        return new ToolCallRequest(name, args);
    }

    private static string? OptionalString(JsonObject obj, string key, int errorCode, string errorMessage)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
            return null;
        if (node.GetValueKind() != JsonValueKind.String)
            throw new McpRequestException(errorCode, errorMessage);
        return node.GetValue<string>();
    }
}

internal sealed record ToolCallRequest(string Name, JsonObject Args);

internal sealed class McpRequestException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
