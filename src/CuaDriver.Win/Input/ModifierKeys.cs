namespace CuaDriver.Win.Input;

internal enum ModifierKey
{
    None,
    Alt,
    Control,
    Meta,
    Shift
}

internal static class ModifierKeys
{
    public static ModifierKey Normalize(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "alt" or "option" => ModifierKey.Alt,
            "ctrl" or "control" => ModifierKey.Control,
            "cmd" or "meta" or "win" => ModifierKey.Meta,
            "shift" => ModifierKey.Shift,
            _ => ModifierKey.None
        };
    }
}
