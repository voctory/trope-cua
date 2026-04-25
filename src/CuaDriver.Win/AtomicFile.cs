using System.IO;

namespace CuaDriver.Win;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        Write(path, tempPath => File.WriteAllText(tempPath, contents));
    }

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        Write(path, tempPath => File.WriteAllBytes(tempPath, bytes));
    }

    private static void Write(string path, Action<string> writeTempFile)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory ?? ".",
            $"{Path.GetFileName(fullPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            writeTempFile(tempPath);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup for failed atomic writes.
        }
    }
}
