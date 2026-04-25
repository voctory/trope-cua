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

            McpRequest? request = null;
            JsonNode? idNode = null;
            try
            {
                request = McpRequest.Parse(line);
                idNode = request.Id;
                if (request.IsNotification)
                    continue;

                var response = request.Method switch
                {
                    "initialize" => McpProtocol.Response(request.Id, InitializeResult()),
                    "tools/list" => McpProtocol.Response(request.Id, ToolsListResult()),
                    "tools/call" => McpProtocol.Response(request.Id, await ToolsCallAsync(request, cancellationToken).ConfigureAwait(false)),
                    _ => McpProtocol.Error(request.Id, -32601, $"Unknown method: {request.Method}")
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

    private async Task<JsonObject> ToolsCallAsync(McpRequest request, CancellationToken ct)
    {
        var call = request.ToolCall();
        var result = await _registry.InvokeAsync(call.Name, call.Args, _context, ct).ConfigureAwait(false);
        return McpProtocol.ToolCallResult(result);
    }
}
