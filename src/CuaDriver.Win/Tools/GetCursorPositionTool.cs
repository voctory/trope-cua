using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

public sealed class GetCursorPositionTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new("get_cursor_position", "Return the real cursor position. Read-only diagnostic.", JsonArgs.Schema(), ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetCursorPos(out var p))
        {
            return Task.FromResult(ToolResult.Text(ToolText.ErrorPrefix + "GetCursorPos failed.", new JsonObject
            {
                ["ok"] = false,
                ["route"] = "user32.getcursorpos"
            }, isError: true));
        }

        return Task.FromResult(ToolResult.Text($"{ToolText.OkPrefix}cursor x={p.X} y={p.Y}", new JsonObject
        {
            ["ok"] = true,
            ["route"] = "user32.getcursorpos",
            ["x"] = p.X,
            ["y"] = p.Y
        }));
    }
}
