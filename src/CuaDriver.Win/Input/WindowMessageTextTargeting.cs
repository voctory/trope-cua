using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Input;

internal static class WindowMessageTextTargeting
{
    public static IntPtr FindTextInputTarget(IntPtr root)
    {
        var best = IntPtr.Zero;
        var bestScore = 0;
        NativeMethods.EnumChildWindows(root, (child, _) =>
        {
            try
            {
                var cls = NativeMethods.GetClassName(child).ToLowerInvariant();
                var score = TextInputClassScore(cls);
                if (score > bestScore)
                {
                    best = child;
                    bestScore = score;
                }
            }
            catch
            {
                // Ignore transient child windows.
            }
            return true;
        }, IntPtr.Zero);

        return best != IntPtr.Zero ? best : root;
    }

    private static int TextInputClassScore(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return 0;
        if (className.Contains("richedit", StringComparison.Ordinal))
            return 100;
        if (className.Contains("edit", StringComparison.Ordinal))
            return 90;
        if (className.Contains("scintilla", StringComparison.Ordinal))
            return 80;
        if (className.Contains("text", StringComparison.Ordinal))
            return 50;
        return 0;
    }
}
