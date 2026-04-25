using System.Diagnostics;
using System.IO;
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

    [JsonPropertyName("main_window_id")]
    public long? MainWindowId { get; init; }

    [JsonPropertyName("window_count")]
    public int WindowCount { get; init; }
}

internal static class AppEnumerator
{
    public static IReadOnlyList<AppInfo> RunningApps()
    {
        var windowsByPid = WindowEnumerator.AllWindows().GroupBy(w => w.Pid).ToDictionary(g => g.Key, g => g.ToArray());
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
                    WindowCount = windows?.Length ?? 0
                });
            }
            catch
            {
                // System processes can deny inspection.
            }
        }

        return apps;
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

    private static string? SafePath(Process p)
    {
        try { return p.MainModule?.FileName; }
        catch { return null; }
    }
}
