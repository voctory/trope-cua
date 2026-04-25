using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Tools;

namespace CuaDriver.Win.Mcp;

internal sealed class McpServer
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

            JsonObject request;
            JsonNode? idNode = null;
            try
            {
                request = JsonNode.Parse(line) as JsonObject
                          ?? throw new McpRequestException(-32600, "Invalid request: expected a JSON object.");

                idNode = request["id"]?.DeepClone();
                var method = OptionalString(request, "method", -32600, "Invalid request: method must be a string.") ?? "";

                if (method.StartsWith("notifications/", StringComparison.Ordinal))
                    continue;

                if (string.IsNullOrWhiteSpace(method))
                    throw new McpRequestException(-32600, "Invalid request: missing method.");

                var response = method switch
                {
                    "initialize" => McpProtocol.Response(idNode, InitializeResult()),
                    "tools/list" => McpProtocol.Response(idNode, ToolsListResult()),
                    "tools/call" => McpProtocol.Response(idNode, await ToolsCallAsync(request, cancellationToken).ConfigureAwait(false)),
                    _ => McpProtocol.Error(idNode, -32601, $"Unknown method: {method}")
                };

                await WriteResponseAsync(response, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                await WriteResponseAsync(McpProtocol.Error(null, -32700, $"Parse error: {ex.Message}"), cancellationToken).ConfigureAwait(false);
            }
            catch (McpRequestException ex)
            {
                await WriteResponseAsync(McpProtocol.Error(idNode, ex.Code, ex.Message), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WriteResponseAsync(McpProtocol.Error(idNode, -32603, $"{ex.GetType().Name}: {ex.Message}"), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteResponseAsync(JsonObject response, CancellationToken cancellationToken)
    {
        Console.WriteLine(response.ToJsonString(JsonUtil.LineSerializerOptions));
        await Console.Out.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject InitializeResult() => new()
    {
        ["protocolVersion"] = "2024-11-05",
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
        ["serverInfo"] = new JsonObject { ["name"] = "cua-driver-win", ["version"] = "0.1.0" },
        ["instructions"] = ToolDescriptions.AgentInstructions
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
                ["inputSchema"] = tool.Definition.InputSchema.DeepClone(),
                ["annotations"] = new JsonObject
                {
                    ["readOnlyHint"] = tool.Definition.ReadOnly,
                    ["destructiveHint"] = tool.Definition.Destructive,
                    ["idempotentHint"] = tool.Definition.Idempotent,
                    ["openWorldHint"] = tool.Definition.OpenWorld
                }
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    private async Task<JsonObject> ToolsCallAsync(JsonObject request, CancellationToken ct)
    {
        var p = request["params"] is null
            ? new JsonObject()
            : request["params"] as JsonObject ?? throw new McpRequestException(-32602, "tools/call params must be a JSON object");
        var name = OptionalString(p, "name", -32602, "tools/call params.name must be a string")
                   ?? throw new McpRequestException(-32602, "tools/call missing params.name");
        var args = p["arguments"] is null
            ? new JsonObject()
            : p["arguments"] as JsonObject ?? throw new McpRequestException(-32602, "tools/call params.arguments must be a JSON object");
        var result = await _registry.InvokeAsync(name, args, _context, ct).ConfigureAwait(false);

        return McpProtocol.ToolCallResult(result);
    }

    private static string? OptionalString(JsonObject obj, string key, int errorCode, string errorMessage)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
            return null;
        if (node.GetValueKind() != JsonValueKind.String)
            throw new McpRequestException(errorCode, errorMessage);
        return node.GetValue<string>();
    }

    private sealed class McpRequestException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }
}
