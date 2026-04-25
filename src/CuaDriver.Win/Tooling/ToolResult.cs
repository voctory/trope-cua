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
