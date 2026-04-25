namespace CuaDriver.Win.Input;

internal static class WindowMessageKeyMapping
{
    public static int VirtualKey(string key)
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

    public static int MouseModifierFlags(IEnumerable<string> modifiers)
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
}
