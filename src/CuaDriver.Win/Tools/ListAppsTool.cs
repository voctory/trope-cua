using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Tools;

internal sealed class ListAppsTool : IDriverTool
{
    public ToolDefinition Definition { get; } = new(
        "list_apps",
        ToolDescriptions.ListApps,
        JsonArgs.Schema(),
        ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var apps = AppEnumerator.RunningApps();
        var shortcuts = AppEnumerator.StartMenuShortcuts();

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{ToolText.OkPrefix}Found {apps.Count} running GUI app(s). Start Menu shortcuts detected: {shortcuts.Count}.");
        foreach (var app in apps)
        {
            sb.Append("- ").Append(app.Name)
              .Append(" pid ").Append(app.Pid)
              .Append(" windows=").Append(app.WindowCount);
            if (app.MainWindowId is { } hwnd)
                sb.Append(" main_window_id=").Append(hwnd);
            if (!string.IsNullOrWhiteSpace(app.Path))
                sb.Append(" path=\"").Append(app.Path).Append('"');
            sb.AppendLine();
        }

        return Task.FromResult(ToolResult.Text(sb.ToString().TrimEnd(), new JsonObject
        {
            ["running_count"] = apps.Count,
            ["start_menu_shortcut_count"] = shortcuts.Count,
            ["apps"] = ToolJson.Array(apps, app => new JsonObject
            {
                ["name"] = app.Name,
                ["pid"] = app.Pid,
                ["running"] = app.Running,
                ["path"] = app.Path,
                ["main_window_id"] = app.MainWindowId,
                ["window_count"] = app.WindowCount
            })
        }));
    }
}
