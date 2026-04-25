using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

public sealed class ReplayTrajectoryTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "replay_trajectory",
        "Replay a recorded trajectory by invoking each turn-NNNNN/action.json tool call in lexical order.",
        JsonArgs.RequiredSchema(["dir"],
            ("dir", JsonArgs.Prop("string", "Trajectory directory previously written by set_recording.")),
            ("delay_ms", JsonArgs.Prop("integer", "Milliseconds to sleep between turns. Default 500.")),
            ("stop_on_error", JsonArgs.Prop("boolean", "Stop replay on the first tool error. Default true."))),
        Destructive: true,
        Idempotent: false);

    public async Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var rawDir = JsonArgs.RequiredString(args, "dir");
        var dir = ExpandPath(rawDir);
        if (!Directory.Exists(dir))
            return ToolResult.Error($"Trajectory directory does not exist: {dir}");

        var delayMs = Math.Clamp(JsonArgs.OptionalInt(args, "delay_ms") ?? 500, 0, 10_000);
        var stopOnError = JsonArgs.OptionalBool(args, "stop_on_error", true);
        var turnDirs = Directory.EnumerateDirectories(dir, "turn-*")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (turnDirs.Length == 0)
            return ToolResult.Error($"No turn-NNNNN folders found under {dir}.");

        var attempted = 0;
        var succeeded = 0;
        var failed = 0;
        var turns = new JsonArray();
        ReplayFailure? firstFailure = null;

        for (var i = 0; i < turnDirs.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var turnDir = turnDirs[i];
            var parsed = ParseActionJson(Path.Combine(turnDir, "action.json"));
            if (parsed is null)
                continue;

            attempted++;
            var result = await context.Registry.InvokeAsync(parsed.Value.Tool, parsed.Value.Arguments, context, cancellationToken).ConfigureAwait(false);
            turns.Add(new JsonObject
            {
                ["turn"] = Path.GetFileName(turnDir),
                ["tool"] = parsed.Value.Tool,
                ["ok"] = !result.IsError,
                ["result_summary"] = FirstText(result),
                ["result_structured"] = result.StructuredContent?.DeepClone()
            });
            if (result.IsError)
            {
                failed++;
                firstFailure ??= new ReplayFailure(Path.GetFileName(turnDir), parsed.Value.Tool, FirstText(result));
                if (stopOnError)
                    break;
            }
            else
            {
                succeeded++;
            }

            if (delayMs > 0 && i < turnDirs.Length - 1)
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        var summary = $"replay {Path.GetFileName(dir)}: attempted={attempted} succeeded={succeeded} failed={failed}";
        if (firstFailure is not null)
            summary += $" first_failure={firstFailure.Turn}:{firstFailure.Tool}";

        var structured = new JsonObject
        {
            ["directory"] = dir,
            ["attempted"] = attempted,
            ["succeeded"] = succeeded,
            ["failed"] = failed,
            ["stop_on_error"] = stopOnError,
            ["turns"] = turns
        };
        if (firstFailure is not null)
        {
            structured["first_failure"] = new JsonObject
            {
                ["turn"] = firstFailure.Turn,
                ["tool"] = firstFailure.Tool,
                ["error"] = firstFailure.Error
            };
        }

        return ToolResult.Text("✅ " + summary, structured, failed > 0 && stopOnError);
    }

    private static ParsedAction? ParseActionJson(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("tool", out var toolNode))
                return null;
            var tool = toolNode.GetString();
            if (string.IsNullOrWhiteSpace(tool))
                return null;

            var args = new JsonObject();
            if (document.RootElement.TryGetProperty("arguments", out var argumentsNode) &&
                argumentsNode.ValueKind == JsonValueKind.Object)
            {
                args = JsonNode.Parse(argumentsNode.GetRawText())?.AsObject() ?? new JsonObject();
            }

            return new ParsedAction(tool, args);
        }
        catch
        {
            return null;
        }
    }

    private static string FirstText(ToolResult result) =>
        result.Content.FirstOrDefault(c => c.Type == "text" && c.Text is not null)?.Text ?? "tool reported an error";

    private static string ExpandPath(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return Path.GetFullPath(path);
    }

    private readonly record struct ParsedAction(string Tool, JsonObject Arguments);
    private sealed record ReplayFailure(string Turn, string Tool, string Error);
}
