using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CuaDriver.Win.Mcp;

internal static class DaemonRegistry
{
    public static void Write(DaemonInstanceRecord record)
    {
        AtomicFile.WriteAllText(InstanceRecordPath(record.InstanceId), JsonSerializer.Serialize(record, JsonUtil.SerializerOptions));
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

    private static string RegistryDirectory => Path.Combine(DriverConfig.ConfigDirectory, "daemons");

    private static string InstanceRecordPath(string instanceId) => Path.Combine(RegistryDirectory, $"{DriverInstance.Normalize(instanceId)}.json");
}
