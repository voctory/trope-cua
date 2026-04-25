using System.Text.Json.Serialization;

namespace CuaDriver.Win.Tooling;

internal sealed record ContentBlock
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
