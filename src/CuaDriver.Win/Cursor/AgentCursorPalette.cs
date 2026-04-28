using System.Drawing;
using System.Text.Json.Nodes;

namespace CuaDriver.Win.Cursor;

internal sealed record AgentCursorPalette(
    string Name,
    Color CursorStart,
    Color CursorMid,
    Color CursorEnd,
    Color BloomOuter,
    Color BloomInner)
{
    public static AgentCursorPalette ForInstance(string instanceId)
    {
        if (string.Equals(instanceId, DriverInstance.DefaultId, StringComparison.Ordinal))
            return Default;

        var index = StableIndex(instanceId, Alternates.Length);
        return Alternates[index];
    }

    public static AgentCursorPalette ForNameOrInstance(string? name, string instanceId)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (string.Equals(name, Default.Name, StringComparison.Ordinal))
                return Default;

            var match = Alternates.FirstOrDefault(palette => string.Equals(palette.Name, name, StringComparison.Ordinal));
            if (match is not null)
                return match;
        }

        return ForInstance(instanceId);
    }

    public static IReadOnlyList<string> AlternateNames => Alternates.Select(palette => palette.Name).ToArray();

    public JsonObject ToJsonObject() => new()
    {
        ["name"] = Name,
        ["cursor_start"] = Hex(CursorStart),
        ["cursor_mid"] = Hex(CursorMid),
        ["cursor_end"] = Hex(CursorEnd),
        ["bloom_outer"] = Hex(BloomOuter),
        ["bloom_inner"] = Hex(BloomInner)
    };

    private static readonly AgentCursorPalette Default = new(
        "default_blue",
        Color.FromArgb(219, 238, 255),
        Color.FromArgb(94, 192, 232),
        Color.FromArgb(84, 205, 160),
        Color.FromArgb(188, 232, 252),
        Color.FromArgb(238, 248, 255));

    private static readonly AgentCursorPalette[] Alternates =
    [
        new(
            "soft_purple",
            Color.FromArgb(238, 226, 255),
            Color.FromArgb(178, 132, 255),
            Color.FromArgb(118, 194, 255),
            Color.FromArgb(214, 188, 255),
            Color.FromArgb(246, 238, 255)),
        new(
            "rose_gold",
            Color.FromArgb(255, 231, 238),
            Color.FromArgb(247, 132, 170),
            Color.FromArgb(255, 181, 108),
            Color.FromArgb(255, 190, 211),
            Color.FromArgb(255, 243, 232)),
        new(
            "mint_lime",
            Color.FromArgb(226, 255, 240),
            Color.FromArgb(96, 218, 174),
            Color.FromArgb(178, 229, 72),
            Color.FromArgb(178, 245, 217),
            Color.FromArgb(241, 255, 231)),
        new(
            "amber",
            Color.FromArgb(255, 244, 214),
            Color.FromArgb(244, 178, 66),
            Color.FromArgb(255, 126, 92),
            Color.FromArgb(255, 219, 140),
            Color.FromArgb(255, 248, 225)),
        new(
            "aqua",
            Color.FromArgb(221, 252, 255),
            Color.FromArgb(76, 204, 224),
            Color.FromArgb(63, 222, 166),
            Color.FromArgb(172, 241, 249),
            Color.FromArgb(236, 255, 251)),
        new(
            "orchid",
            Color.FromArgb(252, 228, 255),
            Color.FromArgb(221, 113, 236),
            Color.FromArgb(255, 139, 196),
            Color.FromArgb(237, 181, 246),
            Color.FromArgb(255, 239, 252)),
        new(
            "crimson",
            Color.FromArgb(255, 226, 226),
            Color.FromArgb(232, 82, 98),
            Color.FromArgb(150, 94, 255),
            Color.FromArgb(255, 168, 178),
            Color.FromArgb(255, 240, 241)),
        new(
            "chartreuse",
            Color.FromArgb(247, 255, 218),
            Color.FromArgb(184, 220, 54),
            Color.FromArgb(72, 190, 119),
            Color.FromArgb(224, 247, 128),
            Color.FromArgb(249, 255, 232)),
        new(
            "cobalt",
            Color.FromArgb(226, 235, 255),
            Color.FromArgb(80, 126, 236),
            Color.FromArgb(91, 219, 222),
            Color.FromArgb(170, 195, 255),
            Color.FromArgb(239, 246, 255))
    ];

    private static int StableIndex(string value, int count)
    {
        var lastSeparator = value.LastIndexOfAny(['-', '_', '.']);
        var suffix = lastSeparator >= 0 ? value[(lastSeparator + 1)..] : value;
        if (int.TryParse(suffix, out var numeric) && numeric > 0)
            return (numeric - 1) % count;

        if (suffix.Length == 1 && char.IsAsciiLetter(suffix[0]))
            return (char.ToLowerInvariant(suffix[0]) - 'a') % count;

        var hash = 2166136261u;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return (int)(hash % (uint)count);
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
