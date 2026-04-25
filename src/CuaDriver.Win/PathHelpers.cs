using System.IO;

namespace CuaDriver.Win;

internal static class PathHelpers
{
    public static string ExpandUserPath(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return path;
    }

    public static string ExpandUserPathToFullPath(string path) => Path.GetFullPath(ExpandUserPath(path));
}
