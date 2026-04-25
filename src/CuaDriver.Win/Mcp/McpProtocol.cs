using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Mcp;

internal static class McpProtocol
{
    public static JsonObject Response(JsonNode? id, JsonObject result)
    {
        var obj = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = result };
        if (id is not null)
            obj["id"] = id;
        return obj;
    }

    public static JsonObject Error(JsonNode? id, int code, string message)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
        if (id is not null)
            obj["id"] = id;
        return obj;
    }

    public static JsonObject ToolCallResult(ToolResult result)
    {
        var content = new JsonArray();
        foreach (var block in result.Content)
        {
            var obj = new JsonObject { ["type"] = block.Type };
            if (block.Text is not null)
                obj["text"] = block.Text;
            if (block.Data is not null)
                obj["data"] = block.Data;
            if (block.MimeType is not null)
                obj["mimeType"] = block.MimeType;
            content.Add(obj);
        }

        var response = new JsonObject { ["content"] = content, ["isError"] = result.IsError };
        var structured = result.StructuredContent?.DeepClone() as JsonObject;
        if (structured is not null)
            response["structuredContent"] = structured;
        return response;
    }
}
