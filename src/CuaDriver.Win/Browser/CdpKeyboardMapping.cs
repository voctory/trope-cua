using System.Text.Json.Nodes;
using CuaDriver.Win.Input;

namespace CuaDriver.Win.Browser;

internal static class CdpKeyboardMapping
{
    public static JsonObject? Event(string type, string key, IReadOnlyCollection<string> modifiers)
    {
        var normalized = key.Trim();
        var lower = normalized.ToLowerInvariant();
        var shift = modifiers.Any(m => ModifierKeys.Normalize(m) == ModifierKey.Shift);
        var vk = WindowMessageKeyMapping.VirtualKey(key);
        if (vk == 0)
            return null;

        var keyValue = lower switch
        {
            "enter" or "return" => "Enter",
            "escape" or "esc" => "Escape",
            "tab" => "Tab",
            "space" => " ",
            "backspace" => "Backspace",
            "delete" => "Delete",
            "left" => "ArrowLeft",
            "up" => "ArrowUp",
            "right" => "ArrowRight",
            "down" => "ArrowDown",
            "home" => "Home",
            "end" => "End",
            "pageup" => "PageUp",
            "pagedown" => "PageDown",
            var s when s.Length == 1 && char.IsLetter(s[0]) => shift ? s.ToUpperInvariant() : s,
            var s when s.Length == 1 && char.IsDigit(s[0]) => s,
            var s when s.StartsWith('f') && int.TryParse(s[1..], out var n) && n is >= 1 and <= 24 => s.ToUpperInvariant(),
            _ => normalized
        };

        var code = lower switch
        {
            "enter" or "return" => "Enter",
            "escape" or "esc" => "Escape",
            "tab" => "Tab",
            "space" => "Space",
            "backspace" => "Backspace",
            "delete" => "Delete",
            "left" => "ArrowLeft",
            "up" => "ArrowUp",
            "right" => "ArrowRight",
            "down" => "ArrowDown",
            "home" => "Home",
            "end" => "End",
            "pageup" => "PageUp",
            "pagedown" => "PageDown",
            var s when s.Length == 1 && char.IsLetter(s[0]) => "Key" + char.ToUpperInvariant(s[0]),
            var s when s.Length == 1 && char.IsDigit(s[0]) => "Digit" + s,
            var s when s.StartsWith('f') && int.TryParse(s[1..], out var n) && n is >= 1 and <= 24 => s.ToUpperInvariant(),
            _ => normalized
        };

        var obj = new JsonObject
        {
            ["type"] = type,
            ["key"] = keyValue,
            ["code"] = code,
            ["windowsVirtualKeyCode"] = vk,
            ["nativeVirtualKeyCode"] = vk,
            ["modifiers"] = CdpInputModifiers.Mask(modifiers)
        };

        if (type == "keyDown" && keyValue.Length == 1 && !char.IsControl(keyValue[0]) && modifiers.All(IsTextSafeModifier))
        {
            obj["text"] = keyValue;
            obj["unmodifiedText"] = keyValue.ToLowerInvariant();
        }

        return obj;
    }

    private static bool IsTextSafeModifier(string modifier)
    {
        var normalized = ModifierKeys.Normalize(modifier);
        return normalized is ModifierKey.None or ModifierKey.Shift;
    }
}
