using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class SetConfigTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "set_config",
        "Set persistent config key. Keys: capture_mode, max_image_dimension, chromium_debugging_port, allow_parent_sendinput, agent_cursor.enabled, agent_cursor.motion.*.",
        JsonArgs.RequiredSchema(["key", "value"],
            ("key", JsonArgs.Prop("string", "Config key.")),
            ("value", JsonArgs.Prop("string", "Config value. JSON strings, numbers, booleans, and null are accepted."))),
        Destructive: true,
        Idempotent: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var key = JsonArgs.RequiredString(args, "key");
        if (!args.TryGetPropertyValue("value", out var value) || value is null)
            return Task.FromResult(ToolResult.Error("Missing required field value. Pass null explicitly to clear nullable config values such as chromium_debugging_port."));

        try
        {
            var next = WithValue(context.State.Config, key, value);
            next.Save();
            context.State.Config = next;
            ApplyLiveConfig(key, next, context);
            return Task.FromResult(ToolResult.Text("✅ " + JsonSerializer.Serialize(next, JsonUtil.SerializerOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error(ex.Message));
        }
    }

    private static DriverConfig WithValue(DriverConfig config, string key, JsonNode value)
    {
        if (key == "agent_cursor" || key == "agent_cursor.motion")
            throw new ArgumentException($"{key} is a subtree, not a leaf.");

        return key.ToLowerInvariant() switch
        {
            "capture_mode" => config with { CaptureMode = DriverConfig.ParseCaptureMode(StringValue(value)) },
            "max_image_dimension" => config with { MaxImageDimension = IntValue(value) },
            "chromium_debugging_port" => config with { ChromiumDebuggingPort = IsNullOrBlank(value) ? null : IntValue(value) },
            "allow_parent_sendinput" => config with { AllowParentSendInput = BoolValue(value) },
            "agent_cursor.enabled" => config with { AgentCursor = config.AgentCursor with { Enabled = BoolValue(value) } },
            "agent_cursor.motion.start_handle" => config with { AgentCursor = config.AgentCursor with { Motion = config.AgentCursor.Motion with { StartHandle = NumberValue(value) } } },
            "agent_cursor.motion.end_handle" => config with { AgentCursor = config.AgentCursor with { Motion = config.AgentCursor.Motion with { EndHandle = NumberValue(value) } } },
            "agent_cursor.motion.arc_size" => config with { AgentCursor = config.AgentCursor with { Motion = config.AgentCursor.Motion with { ArcSize = NumberValue(value) } } },
            "agent_cursor.motion.arc_flow" => config with { AgentCursor = config.AgentCursor with { Motion = config.AgentCursor.Motion with { ArcFlow = NumberValue(value) } } },
            "agent_cursor.motion.spring" => config with { AgentCursor = config.AgentCursor with { Motion = config.AgentCursor.Motion with { Spring = NumberValue(value) } } },
            "agent_cursor.motion.glide_duration_ms" => config with { AgentCursor = config.AgentCursor with { Motion = config.AgentCursor.Motion with { GlideDurationMs = NumberValue(value) } } },
            "agent_cursor.motion.dwell_after_click_ms" => config with { AgentCursor = config.AgentCursor with { Motion = config.AgentCursor.Motion with { DwellAfterClickMs = NumberValue(value) } } },
            "agent_cursor.motion.idle_hide_ms" => config with { AgentCursor = config.AgentCursor with { Motion = config.AgentCursor.Motion with { IdleHideMs = NumberValue(value) } } },
            _ => throw new ArgumentException($"Unknown config key: {key}")
        };
    }

    private static void ApplyLiveConfig(string key, DriverConfig config, ToolContext context)
    {
        if (key.Equals("agent_cursor.enabled", StringComparison.OrdinalIgnoreCase))
            context.State.AgentCursor.SetEnabled(config.AgentCursor.Enabled);
        else if (key.StartsWith("agent_cursor.motion.", StringComparison.OrdinalIgnoreCase))
        {
            var motion = config.AgentCursor.Motion;
            context.State.AgentCursor.UpdateMotion(
                motion.StartHandle,
                motion.EndHandle,
                motion.ArcSize,
                motion.ArcFlow,
                motion.Spring,
                motion.GlideDurationMs,
                motion.DwellAfterClickMs,
                motion.IdleHideMs);
        }
    }

    private static bool IsNullOrBlank(JsonNode value) =>
        value.GetValueKind() == JsonValueKind.Null || string.IsNullOrWhiteSpace(StringValue(value));

    private static string StringValue(JsonNode value) =>
        value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();

    private static int IntValue(JsonNode value) =>
        value.GetValueKind() == JsonValueKind.Number ? value.GetValue<int>() : int.Parse(StringValue(value));

    private static double NumberValue(JsonNode value) =>
        value.GetValueKind() == JsonValueKind.Number ? value.GetValue<double>() : double.Parse(StringValue(value));

    private static bool BoolValue(JsonNode value) =>
        value.GetValueKind() == JsonValueKind.True ||
        (value.GetValueKind() != JsonValueKind.False && bool.Parse(StringValue(value)));
}
