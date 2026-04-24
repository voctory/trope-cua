using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class SetConfigTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "set_config",
        "Set persistent config key. Keys: capture_mode, max_image_dimension, chromium_debugging_port, allow_parent_sendinput.",
        JsonArgs.Schema(
            ("key", JsonArgs.Prop("string", "Config key.")),
            ("value", JsonArgs.Prop("string", "Config value."))),
        Destructive: true,
        Idempotent: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var key = JsonArgs.RequiredString(args, "key");
        var value = JsonArgs.RequiredString(args, "value");
        var next = context.State.Config.With(key, value);
        next.Save();
        context.State.Config = next;
        return Task.FromResult(ToolResult.Text("✅ " + JsonSerializer.Serialize(next, JsonUtil.SerializerOptions)));
    }
}
