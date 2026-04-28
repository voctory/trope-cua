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
        JsonArgs.Schema(
            ("query", JsonArgs.Prop("string", "Optional case-insensitive app name/path/app_id filter.")),
            ("verbose", JsonArgs.Prop("boolean", "Return every matched app with expanded fields. Default false returns compact likely entries."))),
        ReadOnly: true);

    public Task<ToolResult> InvokeAsync(JsonObject args, ToolContext context, CancellationToken cancellationToken)
    {
        var query = JsonArgs.OptionalString(args, "query");
        var verbose = JsonArgs.OptionalBool(args, "verbose");
        var allApps = AppEnumerator.Apps();
        var apps = string.IsNullOrWhiteSpace(query) ? allApps : allApps.Where(app => Matches(app, query!)).ToArray();
        var runningCount = apps.Count(a => a.Running);
        var installedCount = apps.Count(a => !a.Running);
        var shortcuts = AppEnumerator.StartMenuShortcuts();
        var includeLaunchFields = verbose || !string.IsNullOrWhiteSpace(query);
        var shown = verbose ? apps : CompactAppList(apps, includeLaunchFields);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{ToolText.OkPrefix}Found {apps.Count} app(s): {runningCount} running, {installedCount} installed-not-running");
        if (!string.IsNullOrWhiteSpace(query))
            sb.Append(CultureInfo.InvariantCulture, $" matching \"{query}\"");
        if (!verbose)
            sb.Append(CultureInfo.InvariantCulture, $", showing {shown.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $". Start Menu shortcuts detected: {shortcuts.Count}.");
        foreach (var app in shown)
        {
            if (verbose || includeLaunchFields)
                AppendAppLine(sb, app, includeLaunchFields);
            else
                AppendCompactAppLine(sb, app);
        }
        if (!verbose && shown.Count < apps.Count)
            sb.AppendLine("-> Pass verbose=true for every matched app.");

        return Task.FromResult(ToolResult.Text(sb.ToString().TrimEnd(), new JsonObject
        {
            ["total_count"] = allApps.Count,
            ["running_count"] = runningCount,
            ["installed_count"] = installedCount,
            ["start_menu_shortcut_count"] = shortcuts.Count,
            ["shown_count"] = shown.Count,
            ["omitted_count"] = apps.Count - shown.Count,
            ["query"] = query,
            ["verbose"] = verbose,
            ["apps"] = ToolJson.Array(shown, app => verbose ? FullApp(app) : CompactApp(app, includeLaunchFields))
        }));
    }

    private static AppInfo[] CompactAppList(IReadOnlyList<AppInfo> apps, bool includeInstalledSample)
    {
        var shown = apps
            .Where(app => app.Running)
            .Concat(includeInstalledSample ? apps.Where(app => !app.Running).Take(20) : Enumerable.Empty<AppInfo>())
            .Take(40)
            .ToArray();
        return shown.Length > 0 ? shown : apps.Take(includeInstalledSample ? 40 : 12).ToArray();
    }

    private static bool Matches(AppInfo app, string query) =>
        app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (app.Path?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (app.AppId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static void AppendCompactAppLine(StringBuilder sb, AppInfo app)
    {
        sb.Append("- ").Append(app.Name)
          .Append(" pid=").Append(app.Pid)
          .Append(" windows=").Append(app.WindowCount);
        if (app.MainWindowId is { } hwnd)
            sb.Append(" main_window_id=").Append(hwnd);
        sb.AppendLine();
    }

    private static void AppendAppLine(StringBuilder sb, AppInfo app, bool includeLaunchFields)
    {
        sb.Append("- ").Append(app.Name)
          .Append(app.Running ? " running" : " installed")
          .Append(" pid=").Append(app.Pid)
          .Append(" windows=").Append(app.WindowCount);
        if (app.MainWindowId is { } hwnd)
            sb.Append(" main_window_id=").Append(hwnd);
        if (includeLaunchFields && !string.IsNullOrWhiteSpace(app.Path))
            sb.Append(" path=\"").Append(app.Path).Append('"');
        if (includeLaunchFields && !string.IsNullOrWhiteSpace(app.AppId))
            sb.Append(" app_id=\"").Append(app.AppId).Append('"');
        sb.AppendLine();
    }

    private static JsonObject CompactApp(AppInfo app, bool includeLaunchFields)
    {
        var json = new JsonObject
        {
            ["name"] = app.Name,
            ["pid"] = app.Pid,
            ["running"] = app.Running,
            ["main_window_id"] = app.MainWindowId,
            ["window_count"] = app.WindowCount
        };
        if (includeLaunchFields)
        {
            json["path"] = app.Path;
            json["app_id"] = app.AppId;
        }

        return json;
    }

    private static JsonObject FullApp(AppInfo app)
    {
        var json = CompactApp(app, includeLaunchFields: true);
        json["source"] = app.Source;
        return json;
    }
}
