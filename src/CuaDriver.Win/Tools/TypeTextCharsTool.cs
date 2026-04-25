using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class TypeTextCharsTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "type_text_chars",
        ToolDescriptions.TypeTextChars,
        JsonArgs.Schema(
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND.")),
            ("element_index", JsonArgs.Prop("integer", "Optional element index.")),
            ("text", JsonArgs.Prop("string", "Text to type.")),
            ("delay_ms", JsonArgs.Prop("integer", "Delay between characters in milliseconds, 0-200. Default 30.")),
            ("cdp_port", JsonArgs.Prop("integer", "Optional Chromium debugging port."))),
        Destructive: true,
        Idempotent: false,
        OpenWorld: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        if (!args.ContainsKey("delay_ms"))
            args["delay_ms"] = 30;
        return new TypeTextTool().InvokeAsync(args, context, cancellationToken);
    }
}
