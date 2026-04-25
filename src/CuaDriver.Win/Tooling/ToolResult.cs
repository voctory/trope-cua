using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CuaDriver.Win.Tooling;

public sealed record ContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    public static ContentBlock TextBlock(string text) => new() { Type = "text", Text = text };

    public static ContentBlock ImageBlock(byte[] data, string mimeType) =>
        new() { Type = "image", Data = Convert.ToBase64String(data), MimeType = mimeType };
}

public sealed record ToolResult
{
    [JsonPropertyName("content")]
    public List<ContentBlock> Content { get; init; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; init; }

    [JsonPropertyName("structuredContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? StructuredContent { get; init; }

    public static ToolResult Text(string text, bool isError = false) =>
        new() { IsError = isError, Content = [ContentBlock.TextBlock(text)] };

    public static ToolResult Text(string text, JsonObject structuredContent, bool isError = false) =>
        new() { IsError = isError, Content = [ContentBlock.TextBlock(text)], StructuredContent = structuredContent };

    public static ToolResult Error(string text) => Text("❌ " + text, true);

    public ToolResult WithInferredStructuredContent()
    {
        if (StructuredContent is not null)
            return this;

        var structured = TryExtractStructuredContent();
        return structured is null ? this : this with { StructuredContent = structured };
    }

    private JsonObject? TryExtractStructuredContent()
    {
        var text = Content.FirstOrDefault(block => block.Type == "text")?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var jsonStart = text.IndexOf('{');
        if (jsonStart < 0)
            return null;

        try
        {
            return JsonNode.Parse(text[jsonStart..]) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    public string ToCliText()
    {
        var parts = new List<string>();
        foreach (var block in Content)
        {
            if (block.Type == "text" && block.Text is { } text)
                parts.Add(text);
            else if (block.Type == "image")
                parts.Add($"[image {block.MimeType ?? "application/octet-stream"}, {block.Data?.Length ?? 0} base64 chars]");
        }
        return string.Join(Environment.NewLine, parts);
    }
}

public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonObject InputSchema,
    bool ReadOnly = false,
    bool Destructive = false,
    bool Idempotent = true,
    bool OpenWorld = false);

public interface IDriverTool
{
    ToolDefinition Definition { get; }
    Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken);
}

public sealed class ToolContext
{
    public required DriverState State { get; init; }
    public required ToolRegistry Registry { get; init; }
}

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IDriverTool> _tools;
    private static readonly HashSet<string> ActionToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "click",
        "right_click",
        "double_click",
        "scroll",
        "type_text",
        "type_text_chars",
        "press_key",
        "hotkey",
        "set_value",
    };

    public ToolRegistry(IEnumerable<IDriverTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Definition.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IDriverTool> Tools => _tools.Values.OrderBy(t => t.Definition.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool TryGet(string name, out IDriverTool tool)
    {
        if (_tools.TryGetValue(name, out var found))
        {
            tool = found;
            return true;
        }

        tool = null!;
        return false;
    }

    public async Task<ToolResult> InvokeAsync(string name, JsonObject args, ToolContext context, CancellationToken ct)
    {
        if (!TryGet(name, out var tool))
            return ToolResult.Error($"Unknown tool: {name}");

        var shouldKeepAgentCursorAlive = !tool.Definition.Name.Equals("get_agent_cursor_state", StringComparison.OrdinalIgnoreCase);
        if (shouldKeepAgentCursorAlive)
            context.State.AgentCursor.KeepAlive();
        var shouldRecord = ActionToolNames.Contains(tool.Definition.Name);
        var actionStartTimestamp = shouldRecord ? Stopwatch.GetTimestamp() : 0;
        var recordedArgs = shouldRecord ? (JsonObject)args.DeepClone() : null;
        ToolResult result;
        try
        {
            result = await tool.InvokeAsync(args, context, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ToolResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }

        result = result.WithInferredStructuredContent();

        if (shouldRecord && recordedArgs is not null && context.State.Recording.IsEnabled)
            context.State.Recording.Record(tool.Definition.Name, recordedArgs, result, context, actionStartTimestamp);

        if (shouldKeepAgentCursorAlive)
            context.State.AgentCursor.KeepAlive();
        return result;
    }

    public static ToolRegistry CreateDefault(DriverState state)
    {
        IDriverTool[] tools =
        [
            new Tools.ListWindowsTool(),
            new Tools.ListAppsTool(),
            new Tools.LaunchAppTool(),
            new Tools.GetWindowStateTool(),
            new Tools.GetAccessibilityTreeTool(),
            new Tools.ScreenshotTool(),
            new Tools.ZoomTool(),
            new Tools.ClickTool(),
            new Tools.RightClickTool(),
            new Tools.DoubleClickTool(),
            new Tools.TypeTextTool(),
            new Tools.TypeTextCharsTool(),
            new Tools.SetValueTool(),
            new Tools.PressKeyTool(),
            new Tools.HotkeyTool(),
            new Tools.ScrollTool(),
            new Tools.CheckPermissionsTool(),
            new Tools.GetScreenSizeTool(),
            new Tools.GetCursorPositionTool(),
            new Tools.MoveCursorTool(),
            new Tools.GetConfigTool(),
            new Tools.SetConfigTool(),
            new Tools.SetAgentCursorEnabledTool(),
            new Tools.SetAgentCursorMotionTool(),
            new Tools.GetAgentCursorStateTool(),
            new Tools.SetRecordingTool(),
            new Tools.GetRecordingStateTool(),
            new Tools.ReplayTrajectoryTool(),
            new Tools.BrowserEvalTool(),
            new Tools.AppBroadcastInputProbeTool(),
            new Tools.ChildSessionStatusTool(),
            new Tools.ChildSessionStartTool(),
            new Tools.ChildSessionStopTool(),
        ];

        return new ToolRegistry(tools);
    }
}

public static class JsonArgs
{
    public static int RequiredInt(JsonObject args, string name)
    {
        if (args.TryGetPropertyValue(name, out var node) && node is not null)
            return node.GetValue<int>();
        throw new ArgumentException($"Missing required integer field {name}.");
    }

    public static long RequiredLong(JsonObject args, string name)
    {
        if (args.TryGetPropertyValue(name, out var node) && node is not null)
            return node.GetValue<long>();
        throw new ArgumentException($"Missing required integer field {name}.");
    }

    public static double RequiredDouble(JsonObject args, string name)
    {
        if (args.TryGetPropertyValue(name, out var node) && node is not null)
            return node.GetValue<double>();
        throw new ArgumentException($"Missing required number field {name}.");
    }

    public static string RequiredString(JsonObject args, string name)
    {
        if (args.TryGetPropertyValue(name, out var node) && node is not null)
            return node.GetValue<string>();
        throw new ArgumentException($"Missing required string field {name}.");
    }

    public static int? OptionalInt(JsonObject args, string name)
        => args.TryGetPropertyValue(name, out var node) && node is not null ? node.GetValue<int>() : null;

    public static long? OptionalLong(JsonObject args, string name)
        => args.TryGetPropertyValue(name, out var node) && node is not null ? node.GetValue<long>() : null;

    public static double? OptionalDouble(JsonObject args, string name)
        => args.TryGetPropertyValue(name, out var node) && node is not null ? node.GetValue<double>() : null;

    public static bool OptionalBool(JsonObject args, string name, bool defaultValue = false)
        => args.TryGetPropertyValue(name, out var node) && node is not null ? node.GetValue<bool>() : defaultValue;

    public static string? OptionalString(JsonObject args, string name)
        => args.TryGetPropertyValue(name, out var node) && node is not null ? node.GetValue<string>() : null;

    public static string[] OptionalStringArray(JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out var node) || node is null)
            return [];
        if (node is JsonArray arr)
            return arr.Select(n => n?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToArray();
        var scalar = node.GetValue<string>();
        return string.IsNullOrWhiteSpace(scalar) ? [] : [scalar];
    }

    public static JsonObject Schema(params (string Key, JsonNode Value)[] properties)
    {
        var props = new JsonObject();
        foreach (var (key, value) in properties)
            props[key] = value;
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["additionalProperties"] = false
        };
    }

    public static JsonObject RequiredSchema(string[] required, params (string Key, JsonNode Value)[] properties)
    {
        var schema = Schema(properties);
        var requiredArray = new JsonArray();
        foreach (var name in required)
            requiredArray.Add(name);
        schema["required"] = requiredArray;
        return schema;
    }

    public static JsonObject SchemaWithAnyOf(string[] required, string[][] anyOf, params (string Key, JsonNode Value)[] properties)
    {
        var schema = RequiredSchema(required, properties);
        var alternatives = new JsonArray();
        foreach (var alternative in anyOf)
        {
            var requiredArray = new JsonArray();
            foreach (var name in alternative)
                requiredArray.Add(name);
            alternatives.Add(new JsonObject { ["required"] = requiredArray });
        }
        schema["anyOf"] = alternatives;
        return schema;
    }

    public static JsonObject EnumProp(string description, params string[] values)
    {
        var enumValues = new JsonArray();
        foreach (var value in values)
            enumValues.Add(value);
        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = enumValues
        };
    }

    public static JsonObject Prop(string type, string description)
        => new() { ["type"] = type, ["description"] = description };
}
