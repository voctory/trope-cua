using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace CuaDriver.Win.Tooling;

internal static class JsonArgs
{
    public static int RequiredInt(JsonObject args, string name)
    {
        if (TryGet(args, name, out var node))
            return node.GetValue<int>();
        throw new ArgumentException($"Missing required integer field {name}.");
    }

    public static long RequiredLong(JsonObject args, string name)
    {
        if (TryGet(args, name, out var node))
            return node.GetValue<long>();
        throw new ArgumentException($"Missing required integer field {name}.");
    }

    public static double RequiredDouble(JsonObject args, string name)
    {
        if (TryGet(args, name, out var node))
            return node.GetValue<double>();
        throw new ArgumentException($"Missing required number field {name}.");
    }

    public static string RequiredString(JsonObject args, string name)
    {
        if (TryGet(args, name, out var node))
            return node.GetValue<string>();
        throw new ArgumentException($"Missing required string field {name}.");
    }

    public static bool RequiredBool(JsonObject args, string name)
    {
        if (TryGet(args, name, out var node))
            return node.GetValue<bool>();
        throw new ArgumentException($"Missing required boolean field {name}.");
    }

    public static int? OptionalInt(JsonObject args, string name)
        => TryGet(args, name, out var node) ? node.GetValue<int>() : null;

    public static long? OptionalLong(JsonObject args, string name)
        => TryGet(args, name, out var node) ? node.GetValue<long>() : null;

    public static double? OptionalDouble(JsonObject args, string name)
        => TryGet(args, name, out var node) ? node.GetValue<double>() : null;

    public static bool OptionalBool(JsonObject args, string name, bool defaultValue = false)
        => TryGet(args, name, out var node) ? node.GetValue<bool>() : defaultValue;

    public static string? OptionalString(JsonObject args, string name)
        => TryGet(args, name, out var node) ? node.GetValue<string>() : null;

    public static string? OptionalFirstString(JsonObject args, params string[] names)
    {
        foreach (var name in names)
        {
            var value = OptionalString(args, name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    public static int? TryOptionalInt(JsonObject args, string name) => TryOptionalValue<int>(args, name);

    public static long? TryOptionalLong(JsonObject args, string name) => TryOptionalValue<long>(args, name);

    public static double? TryOptionalDouble(JsonObject args, string name) => TryOptionalValue<double>(args, name);

    public static bool? TryOptionalBool(JsonObject args, string name) => TryOptionalValue<bool>(args, name);

    public static string[] OptionalStringArray(JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out var node) || node is null)
            return [];
        if (node is JsonArray arr)
            return arr.Select(n => n?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToArray();
        var scalar = node.GetValue<string>();
        return string.IsNullOrWhiteSpace(scalar) ? [] : [scalar];
    }

    public static string[] OptionalStringArray(JsonObject args, string preferredName, string fallbackName)
    {
        var values = OptionalStringArray(args, preferredName);
        return values.Length == 0 ? OptionalStringArray(args, fallbackName) : values;
    }

    private static bool TryGet(JsonObject args, string name, [NotNullWhen(true)] out JsonNode? node) =>
        args.TryGetPropertyValue(name, out node) && node is not null;

    private static T? TryOptionalValue<T>(JsonObject args, string name)
        where T : struct
    {
        try
        {
            return TryGet(args, name, out var node) ? node.GetValue<T>() : null;
        }
        catch
        {
            return null;
        }
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
        // MCP/OpenAI tool schemas must be a plain object at the top level.
        // Keep alternative argument validation in each tool parser instead.
        _ = anyOf;
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
    {
        var prop = new JsonObject { ["type"] = type, ["description"] = description };
        if (string.Equals(type, "array", StringComparison.Ordinal))
        {
            prop["items"] = new JsonObject { ["type"] = "string" };
        }

        return prop;
    }
}
