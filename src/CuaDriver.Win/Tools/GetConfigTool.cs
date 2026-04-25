using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class GetConfigTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_config", "Return persistent driver config.", JsonArgs.Schema(), ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var node = JsonSerializer.SerializeToNode(context.State.Config, JsonUtil.SerializerOptions)?.AsObject() ?? new JsonObject();
        return Task.FromResult(ToolResult.Text("✅ " + node.ToJsonString(JsonUtil.SerializerOptions), node));
    }
}
