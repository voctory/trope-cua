using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace CuaDriver.Win.Win32;

internal sealed record AppInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("pid")]
    public int Pid { get; init; }

    [JsonPropertyName("running")]
    public bool Running { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("app_id")]
    public string? AppId { get; init; }

    [JsonPropertyName("main_window_id")]
    public long? MainWindowId { get; init; }

    [JsonPropertyName("window_count")]
    public int WindowCount { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

internal static class AppEnumerator
{
    public static IReadOnlyList<AppInfo> Apps()
    {
        var windows = WindowEnumerator.AllWindows();
        var running = RunningApps(windows);
        var installed = AppsFolderApps(windows);
        var entries = new List<AppInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedWindowIds = installed
            .Where(a => a.Running && a.MainWindowId is not null)
            .Select(a => a.MainWindowId!.Value)
            .ToHashSet();

        foreach (var app in installed)
        {
            if (seen.Add(AppKey(app)))
                entries.Add(app);
        }

        foreach (var app in running)
        {
            if (app.Name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
                && app.MainWindowId is { } hwnd
                && installedWindowIds.Contains(hwnd))
            {
                continue;
            }

            if (seen.Add(AppKey(app)))
                entries.Add(app);
        }

        return entries
            .OrderByDescending(a => a.Running)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<AppInfo> RunningApps()
        => RunningApps(WindowEnumerator.AllWindows());

    private static List<AppInfo> RunningApps(IReadOnlyList<WindowInfo> allWindows)
    {
        var windowsByPid = allWindows.GroupBy(w => w.Pid).ToDictionary(g => g.Key, g => g.ToArray());
        var apps = new List<AppInfo>();

        foreach (var proc in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                windowsByPid.TryGetValue(proc.Id, out var windows);
                var main = windows?.FirstOrDefault();
                if ((windows?.Length ?? 0) == 0 && proc.MainWindowHandle == IntPtr.Zero)
                    continue;

                apps.Add(new AppInfo
                {
                    Name = proc.ProcessName,
                    Pid = proc.Id,
                    Running = true,
                    Path = SafePath(proc),
                    MainWindowId = main?.WindowId ?? (proc.MainWindowHandle != IntPtr.Zero ? proc.MainWindowHandle.ToInt64() : null),
                    WindowCount = windows?.Length ?? 0,
                    Source = "process"
                });
            }
            catch
            {
                // System processes can deny inspection.
            }
        }

        return apps;
    }

    public static IReadOnlyList<AppInfo> AppsFolderApps()
        => AppsFolderApps(WindowEnumerator.AllWindows());

    private static AppInfo[] AppsFolderApps(IReadOnlyList<WindowInfo> windows)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
            return Array.Empty<AppInfo>();

        object? shell = null;
        object? folder = null;
        object? items = null;
        var apps = new List<AppInfo>();
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return Array.Empty<AppInfo>();

            folder = shellType.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, ["shell:AppsFolder"], CultureInfo.InvariantCulture);
            if (folder is null)
                return Array.Empty<AppInfo>();

            items = folder.GetType().InvokeMember("Items", System.Reflection.BindingFlags.InvokeMethod, null, folder, null, CultureInfo.InvariantCulture);
            if (items is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var name = ReadComString(item, "Name");
                    var appsFolderPath = ReadComString(item, "Path");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appsFolderPath))
                        continue;

                    var matched = MatchInstalledAppWindow(name, windows);
                    var isPathOrUri = IsFullyQualifiedPathOrUri(appsFolderPath);
                    apps.Add(new AppInfo
                    {
                        Name = name,
                        Pid = matched?.Pid ?? 0,
                        Running = matched is not null,
                        Path = isPathOrUri ? appsFolderPath : null,
                        AppId = isPathOrUri ? null : appsFolderPath,
                        MainWindowId = matched?.WindowId,
                        WindowCount = matched is null ? 0 : windows.Count(w => w.Pid == matched.Pid && WindowTitleMatchesAppName(w.Title, name)),
                        Source = "apps_folder"
                    });

                    if (Marshal.IsComObject(item))
                        Marshal.FinalReleaseComObject(item);
                }
            }
        }
        catch
        {
            return Array.Empty<AppInfo>();
        }
        finally
        {
            ReleaseCom(items);
            ReleaseCom(folder);
            ReleaseCom(shell);
        }

        return apps
            .GroupBy(AppKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> StartMenuShortcuts()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        var links = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                links.AddRange(Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories));
            }
            catch
            {
                // ignore inaccessible folders
            }
        }
        return links.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static WindowInfo? MatchInstalledAppWindow(string name, IReadOnlyList<WindowInfo> windows)
        => windows
            .Where(w => w.IsVisible && WindowTitleMatchesAppName(w.Title, name))
            .OrderBy(w => w.Title.Length)
            .FirstOrDefault();

    private static bool WindowTitleMatchesAppName(string title, string name)
        => string.Equals(title.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsFullyQualifiedPathOrUri(string value)
        => Path.IsPathFullyQualified(value) || Uri.TryCreate(value, UriKind.Absolute, out _);

    private static string AppKey(AppInfo app)
    {
        if (!string.IsNullOrWhiteSpace(app.AppId))
            return "app_id:" + app.AppId;
        if (!string.IsNullOrWhiteSpace(app.Path))
            return "path:" + app.Path;
        return "name:" + app.Name;
    }

    private static string? ReadComString(object item, string propertyName)
    {
        try
        {
            return item.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.GetProperty, null, item, null, CultureInfo.InvariantCulture) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj is not null && Marshal.IsComObject(obj))
            Marshal.FinalReleaseComObject(obj);
    }

    private static string? SafePath(Process p)
    {
        try { return p.MainModule?.FileName; }
        catch { return null; }
    }
}
