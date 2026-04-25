using System.Text.Json.Nodes;

namespace CuaDriver.Win;

internal sealed record CliArguments(string[] Args, string? InstanceId, bool InstanceSpecified)
{
    public static CliArguments Parse(string[] args)
    {
        string? instanceId = null;
        var instanceSpecified = false;
        var kept = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--instance")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("--instance requires a value.");
                instanceId = args[++i];
                instanceSpecified = true;
                continue;
            }

            if (arg.StartsWith("--instance=", StringComparison.Ordinal))
            {
                instanceId = arg["--instance=".Length..];
                instanceSpecified = true;
                continue;
            }

            kept.Add(arg);
        }

        return new CliArguments(kept.ToArray(), instanceId, instanceSpecified);
    }

    public static JsonObject ParseToolArguments(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new JsonObject();

        var parsed = JsonNode.Parse(raw);
        if (parsed is not JsonObject obj)
            throw new ArgumentException("Tool arguments must be a JSON object.");
        return obj;
    }
}
