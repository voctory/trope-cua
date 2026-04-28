using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using CuaDriver.Win.Cursor;
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
        var parsed = CliArguments.Parse(args);
        args = parsed.Args;
        var instanceSpecified = parsed.InstanceSpecified;
        var command = args.Length == 0 ? "" : args[0].ToLowerInvariant();
        var instanceId = ResolveRuntimeInstance(parsed.InstanceId, instanceSpecified, command);
        var state = new DriverState(instanceId);
        var registry = ToolRegistry.CreateDefault(state);
        var context = new ToolContext { State = state, Registry = registry };

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            CliOutput.PrintHelp(registry);
            return 0;
        }

        if (command == "mcp")
        {
            var paletteName = CursorPaletteRegistry.Claim(instanceId, "mcp");
            state.AgentCursor.SetPalette(AgentCursorPalette.ForNameOrInstance(paletteName, instanceId));
            try
            {
                await new Mcp.McpServer(registry, context).RunAsync(CancellationToken.None).ConfigureAwait(false);
                return 0;
            }
            finally
            {
                CursorPaletteRegistry.Release(instanceId);
            }
        }

        if (command == "mcp-config")
        {
            PrintMcpConfig(args);
            return 0;
        }

        if (command == "serve")
        {
            await new Mcp.NamedPipeDaemon(registry, context, instanceId).RunAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }

        if (command == "daemon-status" || command == "status")
        {
            var daemon = new Mcp.NamedPipeDaemonClient(instanceId);
            var result = await daemon.TryStatusAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false)
                         ?? ToolResult.Error($"daemon not running on named pipe {daemon.InstancePipeName}");
            CliOutput.PrintResult(result);
            return result.IsError ? 1 : 0;
        }

        if (command == "daemon-list")
        {
            var result = Mcp.NamedPipeDaemon.ListInstances();
            CliOutput.PrintResult(result);
            return 0;
        }

        if (command == "daemon-stop" || command == "daemon-shutdown" || command == "stop")
        {
            if (args.Any(arg => arg.Equals("--all", StringComparison.OrdinalIgnoreCase)))
            {
                var stopAllResult = await Mcp.DaemonControl.StopAllAsync(registry, context).ConfigureAwait(false);
                CliOutput.PrintResult(stopAllResult);
                return stopAllResult.IsError ? 1 : 0;
            }

            var daemon = new Mcp.NamedPipeDaemonClient(instanceId);
            var stopResult = await daemon.TryShutdownAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false)
                         ?? ToolResult.Error($"daemon not running on named pipe {daemon.InstancePipeName}");
            CliOutput.PrintResult(stopResult);
            return stopResult.IsError ? 1 : 0;
        }

        if (command == "tools" || command == "list-tools")
        {
            CliOutput.PrintTools(registry);
            return 0;
        }

        if (command == "describe")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: trope-cua describe <tool>");
                return 64;
            }

            return CliOutput.PrintToolDescription(registry, args[1]) ? 0 : 64;
        }

        if (command == "config")
            return await RunConfigCommandAsync(args, instanceSpecified, registry, context, instanceId).ConfigureAwait(false);

        if (command == "recording")
            return await RunRecordingCommandAsync(args, instanceSpecified, registry, context, instanceId).ConfigureAwait(false);

        if (command == "diagnose")
            return await RunDiagnoseCommandAsync(registry, context, instanceId).ConfigureAwait(false);

        if (command == "doctor")
        {
            Console.WriteLine("No Windows cleanup tasks are currently required.");
            return 0;
        }

        if (command == "update")
        {
            Console.WriteLine("Windows auto-update is not implemented yet.");
            Console.WriteLine("Reinstall from this checkout with: .\\scripts\\install-windows.ps1 -SelfContained");
            return 0;
        }

        if (command == "call")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: trope-cua call <tool> [json]");
                return 64;
            }

            var toolName = args[1];
            var toolArgs = CliArguments.ParseToolArguments(args.Length >= 3 ? args[2] : "{}");
            var daemon = new Mcp.NamedPipeDaemonClient(instanceId);
            var result = await daemon.TryCallAsync(toolName, toolArgs, TimeSpan.FromMinutes(5), CancellationToken.None).ConfigureAwait(false);
            if (result is null)
            {
                result = instanceSpecified
                    ? ToolResult.Error($"daemon not running on named pipe {daemon.InstancePipeName}")
                    : await registry.InvokeAsync(toolName, toolArgs, context, CancellationToken.None).ConfigureAwait(false);
            }

            CliOutput.PrintResult(result);
            return result.IsError ? 1 : 0;
        }

        var directToolName = args[0];
        var directArgs = CliArguments.ParseToolArguments(args.Length >= 2 ? args[1] : "{}");
        var directResult = await registry.InvokeAsync(directToolName, directArgs, context, CancellationToken.None).ConfigureAwait(false);
        CliOutput.PrintResult(directResult);
        return directResult.IsError ? 1 : 0;
    }

    private static async Task<int> RunConfigCommandAsync(
        string[] args,
        bool instanceSpecified,
        ToolRegistry registry,
        ToolContext context,
        string instanceId)
    {
        var subcommand = args.Length >= 2 ? args[1].ToLowerInvariant() : "show";
        switch (subcommand)
        {
            case "show":
                {
                    var result = await InvokeWithDaemonFallbackAsync("get_config", new JsonObject(), instanceSpecified, registry, context, instanceId).ConfigureAwait(false);
                    CliOutput.PrintResult(result);
                    return result.IsError ? 1 : 0;
                }
            case "get":
                {
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("Usage: trope-cua config get <key>");
                        return 64;
                    }

                    var value = GetDottedValue(context.State.Config.ToJsonObject(), args[2]);
                    if (value is null)
                    {
                        Console.Error.WriteLine($"Unknown config key: {args[2]}");
                        return 64;
                    }

                    Console.WriteLine(value.ToJsonString(JsonUtil.SerializerOptions));
                    return 0;
                }
            case "set":
                {
                    if (args.Length < 4)
                    {
                        Console.Error.WriteLine("Usage: trope-cua config set <key> <json-value>");
                        return 64;
                    }

                    var result = await InvokeWithDaemonFallbackAsync(
                        "set_config",
                        new JsonObject
                        {
                            ["key"] = args[2],
                            ["value"] = ParseConfigValue(args[3])
                        },
                        instanceSpecified,
                        registry,
                        context,
                        instanceId).ConfigureAwait(false);
                    CliOutput.PrintResult(result);
                    return result.IsError ? 1 : 0;
                }
            case "reset":
                {
                    var next = new DriverConfig();
                    context.State.SaveConfig(next, "reset");
                    CliOutput.PrintResult(ToolResult.JsonText(ToolText.OkPrefix, context.State.Config.ToJsonObject()));
                    return 0;
                }
            default:
                Console.Error.WriteLine("Usage: trope-cua config [show|get|set|reset]");
                return 64;
        }
    }

    private static async Task<int> RunRecordingCommandAsync(
        string[] args,
        bool instanceSpecified,
        ToolRegistry registry,
        ToolContext context,
        string instanceId)
    {
        var subcommand = args.Length >= 2 ? args[1].ToLowerInvariant() : "status";
        var toolName = subcommand == "status" ? "get_recording_state" : "set_recording";
        JsonObject toolArgs;
        switch (subcommand)
        {
            case "start":
                var outputDir = args.Length >= 3
                    ? args[2]
                    : Path.Combine(
                        Environment.CurrentDirectory,
                        "recordings",
                        DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                toolArgs = new JsonObject
                {
                    ["enabled"] = true,
                    ["output_dir"] = outputDir
                };
                break;
            case "stop":
                toolArgs = new JsonObject { ["enabled"] = false };
                break;
            case "status":
                toolArgs = new JsonObject();
                break;
            default:
                Console.Error.WriteLine("Usage: trope-cua recording [start [output_dir]|stop|status]");
                return 64;
        }

        var result = await InvokeWithDaemonFallbackAsync(toolName, toolArgs, instanceSpecified, registry, context, instanceId).ConfigureAwait(false);
        CliOutput.PrintResult(result);
        return result.IsError ? 1 : 0;
    }

    private static async Task<int> RunDiagnoseCommandAsync(ToolRegistry registry, ToolContext context, string instanceId)
    {
        Console.WriteLine("== check_permissions ==");
        CliOutput.PrintResult(await registry.InvokeAsync("check_permissions", new JsonObject(), context, CancellationToken.None).ConfigureAwait(false));
        Console.WriteLine();
        Console.WriteLine("== get_config ==");
        CliOutput.PrintResult(await registry.InvokeAsync("get_config", new JsonObject(), context, CancellationToken.None).ConfigureAwait(false));
        Console.WriteLine();
        Console.WriteLine("== daemon-list ==");
        CliOutput.PrintResult(Mcp.NamedPipeDaemon.ListInstances());
        Console.WriteLine();
        Console.WriteLine("== daemon-status ==");
        var daemon = new Mcp.NamedPipeDaemonClient(instanceId);
        CliOutput.PrintResult(
            await daemon.TryStatusAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false)
            ?? ToolResult.Error($"daemon not running on named pipe {daemon.InstancePipeName}"));
        return 0;
    }

    private static async Task<ToolResult> InvokeWithDaemonFallbackAsync(
        string toolName,
        JsonObject toolArgs,
        bool instanceSpecified,
        ToolRegistry registry,
        ToolContext context,
        string instanceId)
    {
        var daemon = new Mcp.NamedPipeDaemonClient(instanceId);
        var result = await daemon.TryCallAsync(toolName, toolArgs, TimeSpan.FromMinutes(5), CancellationToken.None).ConfigureAwait(false);
        if (result is not null)
            return result;

        return instanceSpecified
            ? ToolResult.Error($"daemon not running on named pipe {daemon.InstancePipeName}")
            : await registry.InvokeAsync(toolName, toolArgs, context, CancellationToken.None).ConfigureAwait(false);
    }

    private static JsonNode? GetDottedValue(JsonObject obj, string key)
    {
        JsonNode? current = obj;
        foreach (var part in key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is not JsonObject currentObject ||
                !currentObject.TryGetPropertyValue(part, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static JsonNode? ParseConfigValue(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return JsonValue.Create(value);
        }
    }

    private static void PrintMcpConfig(string[] args)
    {
        var client = ClientOption(args);
        var binary = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "TropeCUA",
            "trope-cua.exe");

        switch (client)
        {
            case null:
                Console.WriteLine(GenericMcpServersSnippet(binary, includeType: false));
                break;
            case "claude":
                Console.WriteLine($"claude mcp add --transport stdio trope-cua -- \"{binary}\" mcp");
                break;
            case "codex":
                Console.WriteLine($"codex mcp add trope-cua -- \"{binary}\" mcp");
                break;
            case "cursor":
                Console.WriteLine(GenericMcpServersSnippet(binary, includeType: true));
                break;
            default:
                Console.Error.WriteLine("Unknown client. Valid: claude, codex, cursor.");
                break;
        }
    }

    private static string? ClientOption(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--client" && i + 1 < args.Length)
                return args[i + 1].ToLowerInvariant();
            if (args[i].StartsWith("--client=", StringComparison.Ordinal))
                return args[i]["--client=".Length..].ToLowerInvariant();
        }

        return null;
    }

    private static string GenericMcpServersSnippet(string binary, bool includeType)
    {
        var typeLine = includeType ? "," + Environment.NewLine + "      \"type\": \"stdio\"" : "";
        return $$"""
        {
          "mcpServers": {
            "trope-cua": {
              "command": "{{binary.Replace("\\", "\\\\")}}",
              "args": ["mcp"]{{typeLine}}
            }
          }
        }
        """;
    }

    private static string ResolveRuntimeInstance(string? parsedInstanceId, bool instanceSpecified, string command)
    {
        if (!command.Equals("mcp", StringComparison.OrdinalIgnoreCase) || instanceSpecified)
            return DriverInstance.Resolve(parsedInstanceId);

        var envInstance = Environment.GetEnvironmentVariable("TROPE_CUA_INSTANCE");
        return string.IsNullOrWhiteSpace(envInstance)
            ? DriverInstance.Normalize($"mcp-{Environment.ProcessId}")
            : DriverInstance.Resolve(parsedInstanceId);
    }
}
