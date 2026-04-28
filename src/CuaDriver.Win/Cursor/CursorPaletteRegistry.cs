using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace CuaDriver.Win.Cursor;

internal sealed record CursorPaletteRecord(string InstanceId, int Pid, string Kind, string StartedAt, string ExePath, string Palette);

internal static class CursorPaletteRegistry
{
    public static string Claim(string instanceId, string kind)
    {
        var normalized = DriverInstance.Normalize(instanceId);
        using var mutex = new Mutex(initiallyOwned: false, MutexName());
        var ownsMutex = false;
        try
        {
            ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(5));
            var palette = Allocate(normalized);
            Write(new CursorPaletteRecord(
                normalized,
                Environment.ProcessId,
                kind,
                DateTimeOffset.UtcNow.ToString("O"),
                Environment.ProcessPath ?? "",
                palette));
            return palette;
        }
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
        }
    }

    public static bool Release(string instanceId)
    {
        try
        {
            var path = RecordPath(instanceId);
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Allocate(string instanceId)
    {
        var used = LiveRecords()
            .Where(record => !string.Equals(record.InstanceId, instanceId, StringComparison.Ordinal))
            .Select(record => record.Palette)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        if (!used.Contains("default_blue"))
            return "default_blue";

        foreach (var name in AgentCursorPalette.AlternateNames)
        {
            if (!used.Contains(name))
                return name;
        }

        return AgentCursorPalette.ForInstance(instanceId).Name;
    }

    private static CursorPaletteRecord[] LiveRecords()
    {
        if (!Directory.Exists(RegistryDirectory))
            return [];

        var records = new List<CursorPaletteRecord>();
        foreach (var path in Directory.EnumerateFiles(RegistryDirectory, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<CursorPaletteRecord>(File.ReadAllText(path), JsonUtil.SerializerOptions);
                if (record is null)
                    continue;

                if (IsLive(record))
                {
                    records.Add(record);
                }
                else
                {
                    TryDelete(path);
                }
            }
            catch
            {
                TryDelete(path);
            }
        }

        return records.ToArray();
    }

    private static bool IsLive(CursorPaletteRecord record)
    {
        try
        {
            var process = Process.GetProcessById(record.Pid);
            if (process.HasExited)
                return false;
            return string.IsNullOrWhiteSpace(record.ExePath)
                   || string.Equals(process.MainModule?.FileName, record.ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void Write(CursorPaletteRecord record)
        => AtomicFile.WriteAllText(RecordPath(record.InstanceId), JsonSerializer.Serialize(record, JsonUtil.SerializerOptions));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }

    private static string RegistryDirectory => Path.Combine(DriverConfig.ConfigDirectory, "cursor-palettes");

    private static string RecordPath(string instanceId) => Path.Combine(RegistryDirectory, $"{DriverInstance.Normalize(instanceId)}.json");

    private static string MutexName() => $@"Local\trope-cua-{UserKey()}-cursor-palette";

    private static string UserKey()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        foreach (var ch in Path.GetInvalidFileNameChars())
            sid = sid.Replace(ch, '_');
        return sid;
    }
}
