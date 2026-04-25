using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal sealed class SetConfigTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "set_config",
        "Set persistent config key. Keys: capture_mode, max_image_dimension, chromium_debugging_port, allow_parent_sendinput, agent_cursor.enabled, agent_cursor.motion.*.",
        JsonArgs.RequiredSchema(["key", "value"],
            ("key", JsonArgs.Prop("string", "Config key.")),
            ("value", ConfigValueSchema())),
        Destructive: true,
        Idempotent: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var key = JsonArgs.RequiredString(args, "key");
        if (!args.TryGetPropertyValue("value", out var value))
            return Task.FromResult(ToolResult.Error("Missing required field value."));

        try
        {
            var next = WithValue(context.State.Config, key, value).Normalize();
            context.State.SaveConfig(next, key);

            return Task.FromResult(ToolResult.JsonText(ToolText.OkPrefix, context.State.Config.ToJsonObject()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error(ex.Message));
        }
    }

    private static DriverConfig WithValue(DriverConfig config, string key, JsonNode? value)
    {
        if (key == "agent_cursor" || key == "agent_cursor.motion")
            throw new ArgumentException($"{key} is a subtree, not a leaf.");

        return key.ToLowerInvariant() switch
        {
            "capture_mode" => config with { CaptureMode = DriverConfig.ParseCaptureMode(StringValue(value)) },
            "max_image_dimension" => config with { MaxImageDimension = IntValue(value) },
            "chromium_debugging_port" => config with { ChromiumDebuggingPort = IsNullOrBlank(value) ? null : DriverConfig.ValidateTcpPort(IntValue(value)) },
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
            "agent_cursor.motion.press_duration_ms" => config with { AgentCursor = config.AgentCursor with { Motion = config.AgentCursor.Motion with { PressDurationMs = NumberValue(value) } } },
            _ => throw new ArgumentException($"Unknown config key: {key}")
        };
    }

    private static bool IsNullOrBlank(JsonNode? value) =>
        value is null || value.GetValueKind() == JsonValueKind.Null || string.IsNullOrWhiteSpace(StringValue(value));

    private static string StringValue(JsonNode? value)
    {
        var node = RequireValue(value);
        return node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
    }

    private static int IntValue(JsonNode? value)
    {
        var node = RequireValue(value);
        return node.GetValueKind() == JsonValueKind.Number ? node.GetValue<int>() : int.Parse(StringValue(node), CultureInfo.InvariantCulture);
    }

    private static double NumberValue(JsonNode? value)
    {
        var node = RequireValue(value);
        var number = node.GetValueKind() == JsonValueKind.Number
            ? node.GetValue<double>()
            : double.Parse(StringValue(node), CultureInfo.InvariantCulture);
        if (!double.IsFinite(number))
            throw new ArgumentException("Config number values must be finite.");
        return number;
    }

    private static bool BoolValue(JsonNode? value)
    {
        var node = RequireValue(value);
        return node.GetValueKind() == JsonValueKind.True ||
            (node.GetValueKind() != JsonValueKind.False && bool.Parse(StringValue(node)));
    }

    private static JsonNode RequireValue(JsonNode? value) =>
        value ?? throw new ArgumentException("Config value cannot be null for this key.");

    private static JsonObject ConfigValueSchema() => new()
    {
        ["description"] = "Config value. JSON strings, numbers, booleans, and null are accepted.",
        ["anyOf"] = new JsonArray
        {
            new JsonObject { ["type"] = "string" },
            new JsonObject { ["type"] = "number" },
            new JsonObject { ["type"] = "boolean" },
            new JsonObject { ["type"] = "null" }
        }
    };
}
