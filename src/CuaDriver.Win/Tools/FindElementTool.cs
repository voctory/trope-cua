using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;

namespace CuaDriver.Win.Tools;

internal sealed class FindElementTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "find_element",
        ToolDescriptions.FindElement,
        JsonArgs.RequiredSchema(["pid", "window_id"],
            ("pid", JsonArgs.Prop("integer", "Target process id.")),
            ("window_id", JsonArgs.Prop("integer", "Target HWND as returned by list_windows.")),
            ("query", JsonArgs.Prop("string", "Substring/token query for element name, automation id, or class.")),
            ("label", JsonArgs.Prop("string", "Alias for query when the selector came from a human-visible label.")),
            ("required_query", JsonArgs.Prop("string", "Optional text that must also be present in the same search zone before matches are returned.")),
            ("requiredQuery", JsonArgs.Prop("string", "Alias for required_query.")),
            ("automation_id", JsonArgs.Prop("string", "AutomationId to match.")),
            ("automationId", JsonArgs.Prop("string", "Alias for automation_id.")),
            ("identifier", JsonArgs.Prop("string", "Automation id, name, or class hint.")),
            ("control_type", JsonArgs.Prop("string", "Optional UIA control type filter, e.g. Edit, Button, link.")),
            ("controlType", JsonArgs.Prop("string", "Alias for control_type.")),
            ("target_zone", JsonArgs.Prop("string", "Optional search zone: browser_chrome, page_content, video_player.")),
            ("targetZone", JsonArgs.Prop("string", "Alias for target_zone.")),
            ("limit", JsonArgs.Prop("integer", "Maximum matches to return. Default 1.")),
            ("max_visited", JsonArgs.Prop("integer", "Maximum control-view nodes to inspect. Default 1500.")),
            ("include_offscreen", JsonArgs.Prop("boolean", "Include offscreen elements. Default false."))),
        ReadOnly: true,
        Idempotent: false);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var pid = JsonArgs.RequiredInt(args, "pid");
        var windowId = JsonArgs.RequiredLong(args, "window_id");
        var query = JsonArgs.OptionalString(args, "query") ?? JsonArgs.OptionalString(args, "label");
        var requiredQuery = JsonArgs.OptionalString(args, "required_query") ?? JsonArgs.OptionalString(args, "requiredQuery");
        var automationId = JsonArgs.OptionalString(args, "automation_id") ?? JsonArgs.OptionalString(args, "automationId");
        var identifier = JsonArgs.OptionalString(args, "identifier");
        var controlType = JsonArgs.OptionalString(args, "control_type") ?? JsonArgs.OptionalString(args, "controlType");
        var targetZone = JsonArgs.OptionalString(args, "target_zone") ?? JsonArgs.OptionalString(args, "targetZone");
        var limit = JsonArgs.OptionalInt(args, "limit") ?? 1;
        var maxVisited = JsonArgs.OptionalInt(args, "max_visited") ?? 1500;
        var includeOffscreen = JsonArgs.OptionalBool(args, "include_offscreen");

        if (!ToolWindows.TryFindForPid(pid, windowId, out var window, out var error))
            return Task.FromResult(error!);

        var stopwatch = Stopwatch.StartNew();
        var snapshot = context.State.UiaTree.FindElements(
            pid,
            windowId,
            query,
            requiredQuery,
            automationId,
            identifier,
            controlType,
            targetZone,
            limit,
            maxVisited,
            includeOffscreen,
            cancellationToken);
        stopwatch.Stop();

        var matches = snapshot.Elements.ToArray();
        var structured = new JsonObject
        {
            ["pid"] = pid,
            ["window_id"] = windowId,
            ["window"] = ToolJson.Window(window),
            ["matched"] = matches.Length > 0,
            ["match_count"] = matches.Length,
            ["elapsed_ms"] = stopwatch.ElapsedMilliseconds,
            ["query"] = query,
            ["required_query"] = requiredQuery,
            ["required_matched"] = snapshot.RequiredMatched,
            ["automation_id"] = automationId,
            ["identifier"] = identifier,
            ["control_type"] = controlType,
            ["target_zone"] = targetZone,
            ["uia"] = new JsonObject
            {
                ["turn_id"] = snapshot.TurnId,
                ["element_count"] = snapshot.ElementCount,
                ["tree_markdown_chars"] = snapshot.TreeMarkdown.Length,
                ["metrics"] = ToolJson.UiSnapshotMetrics(snapshot.Metrics)
            },
            ["matches"] = ToolJson.Array(matches, ToolJson.Element)
        };

        if (matches.Length > 0)
        {
            var first = matches[0];
            structured["element_index"] = first.ElementIndex;
            structured["element"] = ToolJson.Element(first);
        }

        var text = matches.Length > 0
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0}find_element matched {1} element(s) in {2} ms; first element_index={3}",
                ToolText.OkPrefix,
                matches.Length,
                stopwatch.ElapsedMilliseconds,
                matches[0].ElementIndex)
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0}find_element found no matches in {1} ms",
                ToolText.WarningPrefix,
                stopwatch.ElapsedMilliseconds);
        if (!string.IsNullOrWhiteSpace(snapshot.TreeMarkdown))
            text += Environment.NewLine + Environment.NewLine + snapshot.TreeMarkdown;

        return Task.FromResult(ToolResult.Text(text, structured));
    }
}
