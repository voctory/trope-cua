using CuaDriver.Win.Input;

namespace CuaDriver.Win.Browser;

internal static class CdpInputModifiers
{
    public static int Mask(IReadOnlyCollection<string>? modifiers)
    {
        if (modifiers is null || modifiers.Count == 0)
            return 0;

        var mask = 0;
        foreach (var modifier in modifiers)
        {
            switch (ModifierKeys.Normalize(modifier))
            {
                case ModifierKey.Alt:
                    mask |= 1;
                    break;
                case ModifierKey.Control:
                    mask |= 2;
                    break;
                case ModifierKey.Meta:
                    mask |= 4;
                    break;
                case ModifierKey.Shift:
                    mask |= 8;
                    break;
            }
        }

        return mask;
    }
}
