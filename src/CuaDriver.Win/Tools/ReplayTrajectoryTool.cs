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
        var dir = PathHelpers.ExpandUserPathToFullPath(rawDir);
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
            var turnName = Path.GetFileName(turnDir);
            var parsed = ParseActionJson(Path.Combine(turnDir, "action.json"));
            if (!parsed.IsValid)
            {
                failed++;
                var error = parsed.Error ?? "Invalid action.json.";
                firstFailure ??= new ReplayFailure(turnName, parsed.Tool ?? "action.json", error);
                turns.Add(new JsonObject
                {
                    ["turn"] = turnName,
                    ["tool"] = parsed.Tool,
                    ["ok"] = false,
                    ["replay_error"] = true,
                    ["error"] = error
                });
                if (stopOnError)
                    break;

                continue;
            }

            attempted++;
            var result = await context.Registry.InvokeAsync(parsed.Tool!, parsed.Arguments, context, cancellationToken).ConfigureAwait(false);
            turns.Add(new JsonObject
            {
                ["turn"] = turnName,
                ["tool"] = parsed.Tool,
                ["ok"] = !result.IsError,
                ["result_summary"] = result.FirstText("tool reported an error"),
                ["result_structured"] = result.StructuredContent?.DeepClone()
            });
            if (result.IsError)
            {
                failed++;
                firstFailure ??= new ReplayFailure(turnName, parsed.Tool!, result.FirstText("tool reported an error"));
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

    private static ParsedAction ParseActionJson(string path)
    {
        try
        {
            if (!File.Exists(path))
                return ParsedAction.Invalid("Missing action.json.");

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return ParsedAction.Invalid("action.json root must be a JSON object.");

            if (!document.RootElement.TryGetProperty("tool", out var toolNode))
                return ParsedAction.Invalid("action.json is missing required string field tool.");

            var tool = toolNode.GetString();
            if (string.IsNullOrWhiteSpace(tool))
                return ParsedAction.Invalid("action.json field tool must be a non-empty string.");

            var args = new JsonObject();
            if (document.RootElement.TryGetProperty("arguments", out var argumentsNode))
            {
                if (argumentsNode.ValueKind != JsonValueKind.Object)
                    return ParsedAction.Invalid("action.json field arguments must be a JSON object.", tool);

                args = JsonNode.Parse(argumentsNode.GetRawText())?.AsObject() ?? new JsonObject();
            }

            return ParsedAction.Valid(tool, args);
        }
        catch (JsonException ex)
        {
            return ParsedAction.Invalid($"Invalid action.json: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return ParsedAction.Invalid($"Invalid action.json: {ex.Message}");
        }
        catch
        {
            return ParsedAction.Invalid("Failed to parse action.json.");
        }
    }

    private sealed record ParsedAction(bool IsValid, string? Tool, JsonObject Arguments, string? Error)
    {
        public static ParsedAction Valid(string tool, JsonObject arguments) => new(true, tool, arguments, null);

        public static ParsedAction Invalid(string error, string? tool = null) => new(false, tool, new JsonObject(), error);
    }

    private sealed record ReplayFailure(string Turn, string Tool, string Error);
}
