using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using CuaDriver.Win.Cursor;

namespace CuaDriver.Win.Mcp;

internal static class DaemonRegistry
{
    public static void Write(DaemonInstanceRecord record)
    {
        AtomicFile.WriteAllText(InstanceRecordPath(record.InstanceId), JsonSerializer.Serialize(record, JsonUtil.SerializerOptions));
    }

    public static string WriteWithAllocatedCursorPalette(DaemonInstanceRecord record)
    {
        using var mutex = new Mutex(initiallyOwned: false, PaletteMutexName());
        var ownsMutex = false;
        try
        {
            ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(5));
            var palette = AllocateCursorPalette(record.InstanceId);
            Write(record with { CursorPalette = palette });
            return palette;
        }
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
        }
    }

    public static bool Remove(string instanceId)
    {
        try
        {
            var path = InstanceRecordPath(instanceId);
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch
        {
            // Best-effort cleanup; daemon-list ignores stale dead pids.
            return false;
        }
    }

    public static DaemonInstanceRecord[] RegisteredInstances()
    {
        if (!Directory.Exists(RegistryDirectory))
            return [];

        var records = new List<DaemonInstanceRecord>();
        foreach (var path in Directory.EnumerateFiles(RegistryDirectory, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<DaemonInstanceRecord>(File.ReadAllText(path), JsonUtil.SerializerOptions);
                if (record is not null)
                    records.Add(record);
            }
            catch
            {
                // Ignore corrupt/stale registry files.
            }
        }

        return records.OrderBy(record => record.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsInstanceRunning(DaemonInstanceRecord record)
    {
        try
        {
            var process = Process.GetProcessById(record.Pid);
            if (string.IsNullOrWhiteSpace(record.ExePath))
                return !process.HasExited;
            return !process.HasExited && string.Equals(process.MainModule?.FileName, record.ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string AllocateCursorPalette(string instanceId)
    {
        var normalized = DriverInstance.Normalize(instanceId);
        if (string.Equals(normalized, DriverInstance.DefaultId, StringComparison.Ordinal))
            return "default_blue";

        var liveRecords = RegisteredInstances()
            .Where(record => !string.Equals(record.InstanceId, normalized, StringComparison.Ordinal) && IsInstanceRunning(record))
            .ToArray();
        var used = liveRecords
            .Select(record => record.CursorPalette)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in AgentCursorPalette.AlternateNames)
        {
            if (!used.Contains(name))
                return name;
        }

        return AgentCursorPalette.ForInstance(normalized).Name;
    }

    private static string RegistryDirectory => Path.Combine(DriverConfig.ConfigDirectory, "daemons");

    private static string InstanceRecordPath(string instanceId) => Path.Combine(RegistryDirectory, $"{DriverInstance.Normalize(instanceId)}.json");

    private static string PaletteMutexName() => $@"Local\cua-driver-win-{UserKey()}-cursor-palette";

    private static string UserKey()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        foreach (var ch in Path.GetInvalidFileNameChars())
            sid = sid.Replace(ch, '_');
        return sid;
    }
}
