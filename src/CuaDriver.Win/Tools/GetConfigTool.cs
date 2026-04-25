using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class GetConfigTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_config", "Return persistent driver config.", JsonArgs.Schema(), ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.JsonText(ToolText.OkPrefix, context.State.Config.ToJsonObject()));
    }
}
