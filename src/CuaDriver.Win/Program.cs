using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        return MainAsync(args).GetAwaiter().GetResult();
    }

    private static async Task<int> MainAsync(string[] args)
    {
        var state = new DriverState();
        var registry = ToolRegistry.CreateDefault(state);
        var context = new ToolContext { State = state, Registry = registry };

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
            await new Mcp.NamedPipeDaemon(registry, context).RunAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
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
            var daemon = new Mcp.NamedPipeDaemon(registry, context);
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
        Console.WriteLine("cua-driver-win serve");
        Console.WriteLine("cua-driver-win call <tool> [json]");
        Console.WriteLine();
        Console.WriteLine("Tools:");
        foreach (var tool in registry.Tools)
            Console.WriteLine($"  {tool.Definition.Name,-28} {FirstLine(tool.Definition.Description)}");
    }

    private static string FirstLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

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
