using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class GetCursorPositionTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_cursor_position", "Return the real cursor position. Read-only diagnostic.", JsonArgs.Schema(), ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        NativeMethods.GetCursorPos(out var p);
        return Task.FromResult(ToolResult.Text($"✅ cursor x={p.X} y={p.Y}"));
    }
}
