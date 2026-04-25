using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CuaDriver.Win.Tooling;

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

    public static ToolResult ImageText(byte[] data, string mimeType, string text, JsonObject structuredContent, bool isError = false) =>
        new()
        {
            IsError = isError,
            Content = [ContentBlock.ImageBlock(data, mimeType), ContentBlock.TextBlock(text)],
            StructuredContent = structuredContent
        };

    public static ToolResult JsonText(string prefix, JsonObject structuredContent, bool isError = false) =>
        Text(prefix + structuredContent.ToJsonString(JsonUtil.SerializerOptions), structuredContent, isError);

    public static ToolResult Error(string text) => Text(ToolText.ErrorPrefix + text, true);

    public string FirstText(string fallback = "") =>
        Content.FirstOrDefault(c => c.Type == "text" && c.Text is not null)?.Text ?? fallback;

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
