using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Mcp;

public sealed class McpServer
{
    private readonly ToolRegistry _registry;
    private readonly ToolContext _context;

    public McpServer(ToolRegistry registry, ToolContext context)
    {
        _registry = registry;
        _context = context;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string? line;
        while ((line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonObject? request = null;
            try
            {
                request = JsonNode.Parse(line) as JsonObject;
                if (request is null)
                    continue;

                var method = request["method"]?.GetValue<string>() ?? "";
                var idNode = request["id"]?.DeepClone();

                if (method.StartsWith("notifications/", StringComparison.Ordinal))
                    continue;

                var response = method switch
                {
                    "initialize" => Response(idNode, InitializeResult()),
                    "tools/list" => Response(idNode, ToolsListResult()),
                    "tools/call" => Response(idNode, await ToolsCallAsync(request, cancellationToken).ConfigureAwait(false)),
                    _ => Error(idNode, -32601, $"Unknown method: {method}")
                };

                Console.WriteLine(response.ToJsonString(JsonUtil.LineSerializerOptions));
                await Console.Out.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var idNode = request?["id"]?.DeepClone();
                Console.WriteLine(Error(idNode, -32603, $"{ex.GetType().Name}: {ex.Message}").ToJsonString(JsonUtil.LineSerializerOptions));
                await Console.Out.FlushAsync().ConfigureAwait(false);
            }
        }
    }

    private static JsonObject InitializeResult() => new()
    {
        ["protocolVersion"] = "2024-11-05",
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
        ["serverInfo"] = new JsonObject { ["name"] = "cua-driver-win", ["version"] = "0.1.0" }
    };

    private JsonObject ToolsListResult()
    {
        var tools = new JsonArray();
        foreach (var tool in _registry.Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Definition.Name,
                ["description"] = tool.Definition.Description,
                ["inputSchema"] = tool.Definition.InputSchema.DeepClone()
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    private async Task<JsonObject> ToolsCallAsync(JsonObject request, CancellationToken ct)
    {
        var p = request["params"] as JsonObject ?? new JsonObject();
        var name = p["name"]?.GetValue<string>() ?? throw new ArgumentException("tools/call missing params.name");
        var args = p["arguments"] as JsonObject ?? new JsonObject();
        var result = await _registry.InvokeAsync(name, args, _context, ct).ConfigureAwait(false);

        var content = new JsonArray();
        foreach (var block in result.Content)
        {
            var obj = new JsonObject { ["type"] = block.Type };
            if (block.Text is not null) obj["text"] = block.Text;
            if (block.Data is not null) obj["data"] = block.Data;
            if (block.MimeType is not null) obj["mimeType"] = block.MimeType;
            content.Add(obj);
        }

        return new JsonObject { ["content"] = content, ["isError"] = result.IsError };
    }

    private static JsonObject Response(JsonNode? id, JsonObject result)
    {
        var obj = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = result };
        if (id is not null) obj["id"] = id;
        return obj;
    }

    private static JsonObject Error(JsonNode? id, int code, string message)
    {
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
        if (id is not null) obj["id"] = id;
        return obj;
    }
}
