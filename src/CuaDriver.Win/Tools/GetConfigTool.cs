using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal sealed class GetConfigTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_config", "Return persistent driver config.", JsonArgs.Schema(), ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var config = context.State.Config;
        var text = $"{ToolText.OkPrefix}config capture_mode={config.CaptureMode.ToString().ToLowerInvariant()} max_image_dimension={config.MaxImageDimension} agent_cursor_enabled={config.AgentCursor.Enabled.ToString().ToLowerInvariant()}";
        return Task.FromResult(ToolResult.Text(text, config.ToJsonObject()));
    }
}
