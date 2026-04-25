using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win;

public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Invalid JSON arguments: {ex.Message}");
            return 64;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 64;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var state = new DriverState();
        var registry = ToolRegistry.CreateDefault(state);
        var context = new ToolContext { State = state, Registry = registry };
        var parsed = ExtractInstanceArg(args);
        args = parsed.Args;
        var instanceId = parsed.InstanceId;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp(registry);
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        if (command == "mcp")
        {
            await new Mcp.McpServer(registry, context).RunAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }

        if (command == "serve")
        {
            await new Mcp.NamedPipeDaemon(registry, context, instanceId).RunAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }

        if (command == "daemon-status")
        {
            var daemon = new Mcp.NamedPipeDaemon(registry, context, instanceId);
            var result = await daemon.TryStatusAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false)
                         ?? ToolResult.Error($"daemon not running on named pipe {daemon.InstancePipeName}");
            PrintResult(result);
            return result.IsError ? 1 : 0;
        }

        if (command == "daemon-list")
        {
            var result = Mcp.NamedPipeDaemon.ListInstances();
            PrintResult(result);
            return 0;
        }

        if (command == "daemon-stop" || command == "daemon-shutdown")
        {
            if (args.Any(arg => arg.Equals("--all", StringComparison.OrdinalIgnoreCase)))
            {
                var stopAllResult = await StopAllDaemonsAsync(registry, context).ConfigureAwait(false);
                PrintResult(stopAllResult);
                return stopAllResult.IsError ? 1 : 0;
            }

            var daemon = new Mcp.NamedPipeDaemon(registry, context, instanceId);
            var stopResult = await daemon.TryShutdownAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false)
                         ?? ToolResult.Error($"daemon not running on named pipe {daemon.InstancePipeName}");
            PrintResult(stopResult);
            return stopResult.IsError ? 1 : 0;
        }

        if (command == "tools")
        {
            foreach (var tool in registry.Tools)
                Console.WriteLine($"{tool.Definition.Name}\t{FirstLine(tool.Definition.Description)}");
            return 0;
        }

        if (command == "call")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: cua-driver-win call <tool> [json]");
                return 64;
            }

            var toolName = args[1];
            var toolArgs = ParseArgs(args.Length >= 3 ? args[2] : "{}");
            var daemon = new Mcp.NamedPipeDaemon(registry, context, instanceId);
            var result = await daemon.TryCallAsync(toolName, toolArgs, TimeSpan.FromMinutes(5), CancellationToken.None).ConfigureAwait(false)
                         ?? await registry.InvokeAsync(toolName, toolArgs, context, CancellationToken.None).ConfigureAwait(false);
            PrintResult(result);
            return result.IsError ? 1 : 0;
        }

        var directToolName = args[0];
        var directArgs = ParseArgs(args.Length >= 2 ? args[1] : "{}");
        var directResult = await registry.InvokeAsync(directToolName, directArgs, context, CancellationToken.None).ConfigureAwait(false);
        PrintResult(directResult);
        return directResult.IsError ? 1 : 0;
    }

    private static void PrintHelp(ToolRegistry registry)
    {
        Console.WriteLine("cua-driver-win <tool> [json]");
        Console.WriteLine("cua-driver-win mcp");
        Console.WriteLine("cua-driver-win serve [--instance <id>]");
        Console.WriteLine("cua-driver-win daemon-status [--instance <id>]");
        Console.WriteLine("cua-driver-win daemon-list");
        Console.WriteLine("cua-driver-win daemon-stop [--instance <id>|--all]");
        Console.WriteLine("cua-driver-win call [--instance <id>] <tool> [json]");
        Console.WriteLine();
        Console.WriteLine("Tools:");
        foreach (var tool in registry.Tools)
            Console.WriteLine($"  {tool.Definition.Name,-28} {FirstLine(tool.Definition.Description)}");
    }

    private static string FirstLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

    private static (string[] Args, string? InstanceId) ExtractInstanceArg(string[] args)
    {
        string? instanceId = null;
        var kept = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--instance")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("--instance requires a value.");
                instanceId = args[++i];
                continue;
            }

            if (arg.StartsWith("--instance=", StringComparison.Ordinal))
            {
                instanceId = arg["--instance=".Length..];
                continue;
            }

            kept.Add(arg);
        }

        return (kept.ToArray(), instanceId);
    }

    private static async Task<ToolResult> StopAllDaemonsAsync(ToolRegistry registry, ToolContext context)
    {
        var records = Mcp.NamedPipeDaemon.RegisteredInstances();
        var stopped = 0;
        var failed = 0;
        var lines = new List<string> { $"daemon-stop --all: instances={records.Count}" };
        var structured = new JsonArray();

        foreach (var record in records)
        {
            var stale = !Mcp.NamedPipeDaemon.IsInstanceRunning(record);
            ToolResult? result = null;
            var ok = stale;
            if (stale)
            {
                _ = Mcp.NamedPipeDaemon.RemoveInstanceRecord(record.InstanceId);
            }
            else
            {
                var daemon = new Mcp.NamedPipeDaemon(registry, context, record.InstanceId);
                result = await daemon.TryShutdownAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
                ok = result is not null && !result.IsError;
            }

            if (ok)
                stopped++;
            else
                failed++;

            structured.Add(new JsonObject
            {
                ["instance_id"] = record.InstanceId,
                ["pid"] = record.Pid,
                ["ok"] = ok,
                ["stale"] = stale,
                ["message"] = stale ? "stale registry record removed" : result?.ToCliText()
            });
            lines.Add($"- instance={record.InstanceId} pid={record.Pid} ok={ok} stale={stale}");
        }

        lines[0] = (failed == 0 ? "✅ " : "❌ ") + lines[0] + $" stopped={stopped} failed={failed}";
        return ToolResult.Text(string.Join(Environment.NewLine, lines), new JsonObject
        {
            ["stopped"] = stopped,
            ["failed"] = failed,
            ["instances"] = structured
        }, failed > 0);
    }

    private static JsonObject ParseArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new JsonObject();

        var parsed = JsonNode.Parse(raw);
        if (parsed is not JsonObject obj)
            throw new ArgumentException("Tool arguments must be a JSON object.");
        return obj;
    }

    private static void PrintResult(ToolResult result)
    {
        if (Environment.GetEnvironmentVariable("CUA_DRIVER_JSON") == "1")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonUtil.SerializerOptions));
            return;
        }
        Console.WriteLine(result.ToCliText());
    }
}
