using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class GetConfigTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_config", "Return persistent driver config.", JsonArgs.Schema(), ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
        => Task.FromResult(ToolResult.Text("✅ " + JsonSerializer.Serialize(context.State.Config, JsonUtil.SerializerOptions)));
}
