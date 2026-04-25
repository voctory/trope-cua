using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CuaDriver.Win;

internal static class JsonUtil
{
    public static readonly JsonSerializerOptions SerializerOptions = Create(writeIndented: true);
    public static readonly JsonSerializerOptions LineSerializerOptions = Create(writeIndented: false);

    public static JsonObject ToJsonObject<T>(T value) =>
        JsonSerializer.SerializeToNode(value, SerializerOptions)?.AsObject()
        ?? new JsonObject();

    private static JsonSerializerOptions Create(bool writeIndented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
