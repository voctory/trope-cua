using System.IO;

namespace CuaDriver.Win;

internal static class DriverInstance
{
    public const string DefaultId = "default";

    public static string Resolve(string? instanceId) =>
        Normalize(instanceId ?? Environment.GetEnvironmentVariable("CUA_DRIVER_INSTANCE") ?? DefaultId);

    public static string Normalize(string? instanceId)
    {
        var normalized = string.IsNullOrWhiteSpace(instanceId) ? DefaultId : instanceId.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', ';', ' ']))
            normalized = normalized.Replace(ch, '_');

        return normalized.Length > 64 ? normalized[..64] : normalized;
    }
}
