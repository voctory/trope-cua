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
        var apps = AppEnumerator.Apps();
        var runningCount = apps.Count(a => a.Running);
        var installedCount = apps.Count(a => !a.Running);
        var shortcuts = AppEnumerator.StartMenuShortcuts();

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{ToolText.OkPrefix}Found {apps.Count} app(s): {runningCount} running, {installedCount} installed-not-running. Start Menu shortcuts detected: {shortcuts.Count}.");
        foreach (var app in apps.Where(a => a.Running))
        {
            sb.Append("- ").Append(app.Name)
              .Append(" pid ").Append(app.Pid)
              .Append(" windows=").Append(app.WindowCount);
            if (app.MainWindowId is { } hwnd)
                sb.Append(" main_window_id=").Append(hwnd);
            if (!string.IsNullOrWhiteSpace(app.Path))
                sb.Append(" path=\"").Append(app.Path).Append('"');
            if (!string.IsNullOrWhiteSpace(app.AppId))
                sb.Append(" app_id=\"").Append(app.AppId).Append('"');
            sb.AppendLine();
        }

        return Task.FromResult(ToolResult.Text(sb.ToString().TrimEnd(), new JsonObject
        {
            ["running_count"] = runningCount,
            ["installed_count"] = installedCount,
            ["start_menu_shortcut_count"] = shortcuts.Count,
            ["apps"] = ToolJson.Array(apps, app => new JsonObject
            {
                ["name"] = app.Name,
                ["pid"] = app.Pid,
                ["running"] = app.Running,
                ["path"] = app.Path,
                ["app_id"] = app.AppId,
                ["main_window_id"] = app.MainWindowId,
                ["window_count"] = app.WindowCount,
                ["source"] = app.Source
            })
        }));
    }
}
