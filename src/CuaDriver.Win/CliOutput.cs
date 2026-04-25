using System.Text.Json;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win;

internal static class CliOutput
{
    public static void PrintHelp(ToolRegistry registry)
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

    public static void PrintTools(ToolRegistry registry)
    {
        foreach (var tool in registry.Tools)
            Console.WriteLine($"{tool.Definition.Name}\t{FirstLine(tool.Definition.Description)}");
    }

    public static void PrintResult(ToolResult result)
    {
        if (Environment.GetEnvironmentVariable("CUA_DRIVER_JSON") == "1")
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonUtil.SerializerOptions));
            return;
        }

        Console.WriteLine(result.ToCliText());
    }

    private static string FirstLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
}
