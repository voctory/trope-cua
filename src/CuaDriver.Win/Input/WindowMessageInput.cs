using System.ComponentModel;
using System.Runtime.InteropServices;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Input;

internal sealed record WindowMessageDispatch(ActionReceipt Receipt, IntPtr TargetHwnd, POINT ScreenPoint, POINT ClientPoint);

internal static class WindowMessageInput
{
    public static async Task<WindowMessageDispatch> ClickAsync(IntPtr hwnd, double x, double y, int count, bool rightButton, CancellationToken ct, string[]? modifiers = null)
    {
        using var guard = NoRegressionGuard.Capture();
        var resolved = ResolvePointTarget(hwnd, x, y);
        try
        {
            var lparam = NativeMethods.MakeLParam(resolved.ClientPoint.X, resolved.ClientPoint.Y);
            var modifierKeys = (modifiers ?? []).Select(VirtualKey).Where(v => v != 0).Distinct().ToArray();
            var modifierFlags = MouseModifierFlags(modifiers ?? []);
            try
            {
                foreach (var vk in modifierKeys)
                    PostMessageOrThrow(resolved.TargetHwnd, NativeMethods.WM_KEYDOWN, (UIntPtr)vk, IntPtr.Zero);

                var normalizedCount = Math.Max(1, count);
                for (var i = 0; i < normalizedCount; i++)
                {
                    PostMessageOrThrow(resolved.TargetHwnd, NativeMethods.WM_MOUSEMOVE, (UIntPtr)modifierFlags, lparam);
                    var isDoubleClickDown = i == 1 && normalizedCount == 2;
                    if (rightButton)
                    {
                        PostMessageOrThrow(resolved.TargetHwnd, isDoubleClickDown ? NativeMethods.WM_RBUTTONDBLCLK : NativeMethods.WM_RBUTTONDOWN, (UIntPtr)(modifierFlags | 0x0002), lparam);
                        await Task.Delay(35, ct).ConfigureAwait(false);
                        PostMessageOrThrow(resolved.TargetHwnd, NativeMethods.WM_RBUTTONUP, (UIntPtr)modifierFlags, lparam);
                        PostMessageOrThrow(resolved.TargetHwnd, NativeMethods.WM_CONTEXTMENU, UIntPtr.Zero, lparam);
                    }
                    else
                    {
                        PostMessageOrThrow(resolved.TargetHwnd, isDoubleClickDown ? NativeMethods.WM_LBUTTONDBLCLK : NativeMethods.WM_LBUTTONDOWN, (UIntPtr)(modifierFlags | 0x0001), lparam);
                        await Task.Delay(35, ct).ConfigureAwait(false);
                        PostMessageOrThrow(resolved.TargetHwnd, NativeMethods.WM_LBUTTONUP, (UIntPtr)modifierFlags, lparam);
                    }

                    if (i + 1 < normalizedCount)
                        await Task.Delay(80, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                foreach (var vk in modifierKeys.Reverse())
                    _ = NativeMethods.PostMessageW(resolved.TargetHwnd, NativeMethods.WM_KEYUP, (UIntPtr)vk, IntPtr.Zero);
            }

            var receipt = guard.Finish(ActionReceipt.Success(rightButton ? "hwnd.postmessage.right_click" : "hwnd.postmessage.click"));
            return new WindowMessageDispatch(receipt, resolved.TargetHwnd, resolved.ScreenPoint, resolved.ClientPoint);
        }
        catch (Exception ex)
        {
            var receipt = guard.Finish(ActionReceipt.Failure("hwnd.postmessage", ex.Message));
            return new WindowMessageDispatch(receipt, resolved.TargetHwnd, resolved.ScreenPoint, resolved.ClientPoint);
        }
    }

    public static ActionReceipt SetText(IntPtr hwnd, string text)
    {
        using var guard = NoRegressionGuard.Capture();
        var ptr = IntPtr.Zero;
        try
        {
            ptr = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(text);
            if (NativeMethods.SendMessageW(hwnd, NativeMethods.WM_SETTEXT, UIntPtr.Zero, ptr) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WM_SETTEXT failed.");
            return guard.Finish(ActionReceipt.Success("hwnd.wm_settext"));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("hwnd.wm_settext", ex.Message));
        }
        finally
        {
            if (ptr != IntPtr.Zero)
                System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
        }
    }

    public static async Task<ActionReceipt> TypeTextAsync(IntPtr hwnd, string text, CancellationToken ct, int delayMs = 4)
    {
        using var guard = NoRegressionGuard.Capture();
        try
        {
            foreach (var ch in text)
            {
                PostMessageOrThrow(hwnd, NativeMethods.WM_CHAR, (UIntPtr)ch, IntPtr.Zero);
                if (delayMs > 0)
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            return guard.Finish(ActionReceipt.Success("hwnd.wm_char"));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("hwnd.wm_char", ex.Message));
        }
    }

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

    public static async Task<ActionReceipt> PressKeyAsync(IntPtr hwnd, string key, string[] modifiers, CancellationToken ct)
    {
        using var guard = NoRegressionGuard.Capture();
        try
        {
            var modifierKeys = modifiers.Select(VirtualKey).Where(v => v != 0).ToArray();
            try
            {
                foreach (var vk in modifierKeys)
                    PostMessageOrThrow(hwnd, NativeMethods.WM_KEYDOWN, (UIntPtr)vk, IntPtr.Zero);

                var main = VirtualKey(key);
                if (main == 0)
                    return guard.Finish(ActionReceipt.Failure("hwnd.key", $"Unknown key: {key}"));

                PostMessageOrThrow(hwnd, NativeMethods.WM_KEYDOWN, (UIntPtr)main, IntPtr.Zero);
                await Task.Delay(25, ct).ConfigureAwait(false);
                PostMessageOrThrow(hwnd, NativeMethods.WM_KEYUP, (UIntPtr)main, IntPtr.Zero);

                return guard.Finish(ActionReceipt.Success("hwnd.key"));
            }
            finally
            {
                foreach (var vk in modifierKeys.Reverse())
                    _ = NativeMethods.PostMessageW(hwnd, NativeMethods.WM_KEYUP, (UIntPtr)vk, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("hwnd.key", ex.Message));
        }
    }

    public static ActionReceipt Scroll(IntPtr hwnd, double? x, double? y, int delta)
    {
        using var guard = NoRegressionGuard.Capture();
        try
        {
            var resolved = x is not null && y is not null
                ? ResolvePointTarget(hwnd, x.Value, y.Value)
                : ResolvePointTarget(hwnd, CenterLocal(hwnd).X, CenterLocal(hwnd).Y);

            var lparam = NativeMethods.MakeLParam(resolved.ClientPoint.X, resolved.ClientPoint.Y);
            var wparam = NativeMethods.MakeWParam(0, delta);
            PostMessageOrThrow(resolved.TargetHwnd, NativeMethods.WM_MOUSEWHEEL, wparam, lparam);
            return guard.Finish(ActionReceipt.Success("hwnd.wm_mousewheel"));
        }
        catch (Exception ex)
        {
            return guard.Finish(ActionReceipt.Failure("hwnd.wm_mousewheel", ex.Message));
        }
    }

    public static POINT WindowLocalToScreen(IntPtr hwnd, double x, double y)
    {
        var rect = NativeMethods.GetBestWindowRect(hwnd);
        return new POINT(rect.Left + (int)Math.Round(x), rect.Top + (int)Math.Round(y));
    }

    public static WindowMessageDispatch ResolvePointTarget(IntPtr hwnd, double x, double y)
    {
        var screen = WindowLocalToScreen(hwnd, x, y);
        var target = DeepestChildFromScreenPoint(hwnd, screen);
        var client = screen;
        NativeMethods.ScreenToClient(target, ref client);
        return new WindowMessageDispatch(ActionReceipt.Success("hwnd.resolve_point"), target, screen, client);
    }

    public static POINT CenterOf(IntPtr hwnd)
    {
        var rect = NativeMethods.GetBestWindowRect(hwnd);
        return new POINT(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
    }

    public static POINT CenterLocal(IntPtr hwnd)
    {
        var rect = NativeMethods.GetBestWindowRect(hwnd);
        return new POINT(rect.Width / 2, rect.Height / 2);
    }

    private static IntPtr DeepestChildFromScreenPoint(IntPtr root, POINT screen)
    {
        var current = root;
        for (var depth = 0; depth < 16; depth++)
        {
            var client = screen;
            NativeMethods.ScreenToClient(current, ref client);
            var child = NativeMethods.ChildWindowFromPointEx(
                current,
                client,
                NativeMethods.CWP_SKIPINVISIBLE | NativeMethods.CWP_SKIPDISABLED | NativeMethods.CWP_SKIPTRANSPARENT);
            if (child == IntPtr.Zero || child == current || !IsWithinRoot(root, child))
                return current;
            current = child;
        }

        return current;
    }

    private static bool IsWithinRoot(IntPtr root, IntPtr child)
    {
        if (root == child)
            return true;
        if (NativeMethods.IsChild(root, child))
            return true;
        var childRoot = NativeMethods.GetAncestor(child, NativeMethods.GA_ROOT);
        return childRoot == root;
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

    private static int VirtualKey(string key)
    {
        switch (ModifierKeys.Normalize(key))
        {
            case ModifierKey.Control:
                return 0x11;
            case ModifierKey.Shift:
                return 0x10;
            case ModifierKey.Alt:
                return 0x12;
            case ModifierKey.Meta:
                return 0x5B;
        }

        return key.Trim().ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "escape" or "esc" => 0x1B,
            "tab" => 0x09,
            "space" => 0x20,
            "backspace" => 0x08,
            "delete" => 0x2E,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            var s when s.Length == 1 && char.IsLetterOrDigit(s[0]) => char.ToUpperInvariant(s[0]),
            var s when s.StartsWith('f') && int.TryParse(s[1..], out var n) && n is >= 1 and <= 24 => 0x70 + n - 1,
            _ => 0
        };
    }

    private static int MouseModifierFlags(IEnumerable<string> modifiers)
    {
        var flags = 0;
        foreach (var modifier in modifiers)
        {
            switch (ModifierKeys.Normalize(modifier))
            {
                case ModifierKey.Control:
                    flags |= 0x0008;
                    break;
                case ModifierKey.Shift:
                    flags |= 0x0004;
                    break;
            }
        }
        return flags;
    }

    private static void PostMessageOrThrow(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam)
    {
        if (!NativeMethods.PostMessageW(hwnd, message, wParam, lParam))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "PostMessageW failed.");
    }
}
