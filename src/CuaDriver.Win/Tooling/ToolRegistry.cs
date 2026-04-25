using System.Diagnostics;
using System.Text.Json.Nodes;

namespace CuaDriver.Win.Tooling;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IDriverTool> _tools;
    private static readonly HashSet<string> ActionToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "click",
        "right_click",
        "double_click",
        "scroll",
        "type_text",
        "type_text_chars",
        "press_key",
        "hotkey",
        "set_value",
    };

    public ToolRegistry(IEnumerable<IDriverTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Definition.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IDriverTool> Tools => _tools.Values.OrderBy(t => t.Definition.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool TryGet(string name, out IDriverTool tool)
    {
        if (_tools.TryGetValue(name, out var found))
        {
            tool = found;
            return true;
        }

        tool = null!;
        return false;
    }

    public async Task<ToolResult> InvokeAsync(string name, JsonObject args, ToolContext context, CancellationToken ct)
    {
        if (!TryGet(name, out var tool))
            return ToolResult.Error($"Unknown tool: {name}");

        var shouldKeepAgentCursorAlive = !tool.Definition.Name.Equals("get_agent_cursor_state", StringComparison.OrdinalIgnoreCase);
        if (shouldKeepAgentCursorAlive)
            context.State.AgentCursor.KeepAlive();
        var shouldRecord = ActionToolNames.Contains(tool.Definition.Name);
        var actionStartTimestamp = shouldRecord ? Stopwatch.GetTimestamp() : 0;
        var recordedArgs = shouldRecord ? (JsonObject)args.DeepClone() : null;
        ToolResult result;
        try
        {
            result = await tool.InvokeAsync(args, context, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ToolResult.Error($"{ex.GetType().Name}: {ex.Message}");
        }

        result = result.WithInferredStructuredContent();

        if (shouldRecord && recordedArgs is not null && context.State.Recording.IsEnabled)
            context.State.Recording.Record(tool.Definition.Name, recordedArgs, result, context, actionStartTimestamp);

        if (shouldKeepAgentCursorAlive)
            context.State.AgentCursor.KeepAlive();
        return result;
    }

    public static ToolRegistry CreateDefault(DriverState state)
    {
        IDriverTool[] tools =
        [
            new Tools.ListWindowsTool(),
            new Tools.ListAppsTool(),
            new Tools.LaunchAppTool(),
            new Tools.GetWindowStateTool(),
            new Tools.GetAccessibilityTreeTool(),
            new Tools.ScreenshotTool(),
            new Tools.ZoomTool(),
            new Tools.ClickTool(),
            new Tools.RightClickTool(),
            new Tools.DoubleClickTool(),
            new Tools.TypeTextTool(),
            new Tools.TypeTextCharsTool(),
            new Tools.SetValueTool(),
            new Tools.PressKeyTool(),
            new Tools.HotkeyTool(),
            new Tools.ScrollTool(),
            new Tools.CheckPermissionsTool(),
            new Tools.GetScreenSizeTool(),
            new Tools.GetCursorPositionTool(),
            new Tools.MoveCursorTool(),
            new Tools.GetConfigTool(),
            new Tools.SetConfigTool(),
            new Tools.SetAgentCursorEnabledTool(),
            new Tools.SetAgentCursorMotionTool(),
            new Tools.GetAgentCursorStateTool(),
            new Tools.SetRecordingTool(),
            new Tools.GetRecordingStateTool(),
            new Tools.ReplayTrajectoryTool(),
            new Tools.BrowserEvalTool(),
            new Tools.AppBroadcastInputProbeTool(),
            new Tools.ChildSessionStatusTool(),
            new Tools.ChildSessionStartTool(),
            new Tools.ChildSessionStopTool(),
        ];

        return new ToolRegistry(tools);
    }
}
