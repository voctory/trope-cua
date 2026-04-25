using System.Text.Json.Nodes;

namespace CuaDriver.Win.Tooling;

internal interface IDriverTool
{
    ToolDefinition Definition { get; }
    Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken);
}
