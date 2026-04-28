using System.Text.Json;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win;

internal static class CliOutput
{
    public static void PrintHelp(ToolRegistry registry)
    {
        Console.WriteLine("trope-cua <tool> [json]");
        Console.WriteLine("trope-cua mcp");
        Console.WriteLine("trope-cua mcp-config [--client claude|codex|cursor]");
        Console.WriteLine("trope-cua serve [--instance <id>]");
        Console.WriteLine("trope-cua status [--instance <id>]");
        Console.WriteLine("trope-cua daemon-list");
        Console.WriteLine("trope-cua stop [--instance <id>|--all]");
        Console.WriteLine("trope-cua list-tools");
        Console.WriteLine("trope-cua describe <tool>");
        Console.WriteLine("trope-cua config [show|get|set|reset]");
        Console.WriteLine("trope-cua recording [start|stop|status]");
        Console.WriteLine("trope-cua diagnose");
        Console.WriteLine("trope-cua call [--instance <id>] <tool> [json]");
        Console.WriteLine();
        Console.WriteLine("Tools:");
        foreach (var tool in registry.Tools)
            Console.WriteLine($"  {tool.Definition.Name,-28} {FirstLine(tool.Definition.Description)}");
    }

    public static void PrintTools(ToolRegistry registry)
    {
        foreach (var tool in registry.Tools)
            Console.WriteLine($"{tool.Definition.Name}\t{FirstLine(tool.Definition.Description)}");
    }

    public static bool PrintToolDescription(ToolRegistry registry, string name)
    {
        var tool = registry.Tools.FirstOrDefault(t => string.Equals(t.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            Console.Error.WriteLine($"Unknown tool: {name}");
            Console.Error.WriteLine("Available tools:");
            foreach (var available in registry.Tools)
                Console.Error.WriteLine($"  {available.Definition.Name}");
            return false;
        }

        Console.WriteLine($"name: {tool.Definition.Name}");
        Console.WriteLine();
        Console.WriteLine("description:");
        Console.WriteLine(tool.Definition.Description);
        Console.WriteLine();
        Console.WriteLine("input_schema:");
        Console.WriteLine(tool.Definition.InputSchema.ToJsonString(JsonUtil.SerializerOptions));
        return true;
    }

    public static void PrintResult(ToolResult result)
    {
        if (Environment.GetEnvironmentVariable("TROPE_CUA_JSON") == "1")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonUtil.SerializerOptions));
            return;
        }

        Console.WriteLine(result.ToCliText());
    }

    private static string FirstLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
}
