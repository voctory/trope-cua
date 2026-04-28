using System.Text.Json;
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

        if (command == "serve")
        {
            await new Mcp.NamedPipeDaemon(registry, context, instanceId).RunAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }

        if (command == "daemon-status")
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

        if (command == "daemon-stop" || command == "daemon-shutdown")
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

        if (command == "tools")
        {
            CliOutput.PrintTools(registry);
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

    private static string ResolveRuntimeInstance(string? parsedInstanceId, bool instanceSpecified, string command)
    {
        if (!command.Equals("mcp", StringComparison.OrdinalIgnoreCase) || instanceSpecified)
            return DriverInstance.Resolve(parsedInstanceId);

        var envInstance = Environment.GetEnvironmentVariable("CUA_DRIVER_INSTANCE");
        return string.IsNullOrWhiteSpace(envInstance)
            ? DriverInstance.Normalize($"mcp-{Environment.ProcessId}")
            : DriverInstance.Resolve(parsedInstanceId);
    }
}
