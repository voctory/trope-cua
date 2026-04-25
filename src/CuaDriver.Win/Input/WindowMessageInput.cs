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
        var resolved = WindowMessageTargeting.ResolvePointTarget(hwnd, x, y);
        try
        {
            var lparam = NativeMethods.MakeLParam(resolved.ClientPoint.X, resolved.ClientPoint.Y);
            var modifierKeys = (modifiers ?? []).Select(WindowMessageKeyMapping.VirtualKey).Where(v => v != 0).Distinct().ToArray();
            var modifierFlags = WindowMessageKeyMapping.MouseModifierFlags(modifiers ?? []);
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
            var modifierKeys = modifiers.Select(WindowMessageKeyMapping.VirtualKey).Where(v => v != 0).ToArray();
            try
            {
                foreach (var vk in modifierKeys)
                    PostMessageOrThrow(hwnd, NativeMethods.WM_KEYDOWN, (UIntPtr)vk, IntPtr.Zero);

                var main = WindowMessageKeyMapping.VirtualKey(key);
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
                ? WindowMessageTargeting.ResolvePointTarget(hwnd, x.Value, y.Value)
                : WindowMessageTargeting.ResolvePointTarget(hwnd, WindowMessageTargeting.CenterLocal(hwnd).X, WindowMessageTargeting.CenterLocal(hwnd).Y);

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

    private static void PostMessageOrThrow(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam)
    {
        if (!NativeMethods.PostMessageW(hwnd, message, wParam, lParam))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "PostMessageW failed.");
    }
}
