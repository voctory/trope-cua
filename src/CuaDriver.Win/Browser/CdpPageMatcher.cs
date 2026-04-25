using System.Text.Json.Nodes;

namespace CuaDriver.Win.Browser;

internal static class CdpPageMatcher
{
    public static JsonObject? BestPageForWindowTitle(IReadOnlyList<JsonObject> pages, string? windowTitle)
    {
        var normalizedWindowTitle = NormalizeBrowserTitle(windowTitle);
        if (string.IsNullOrWhiteSpace(normalizedWindowTitle))
            return null;

        var best = pages
            .Select(page => new
            {
                Page = page,
                Score = TargetScore(normalizedWindowTitle, page["title"]?.GetValue<string>(), page["url"]?.GetValue<string>())
            })
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();
        return best is not null && best.Score > 0 ? best.Page : null;
    }

    private static int TargetScore(string normalizedWindowTitle, string? pageTitle, string? pageUrl)
    {
        var title = NormalizeTitle(pageTitle);
        var url = NormalizeTitle(pageUrl);
        if (!string.IsNullOrWhiteSpace(title))
        {
            if (normalizedWindowTitle.Equals(title, StringComparison.OrdinalIgnoreCase))
                return 100;
            if (normalizedWindowTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
                return 80;
            if (title.Contains(normalizedWindowTitle, StringComparison.OrdinalIgnoreCase))
                return 60;
        }

        if (!string.IsNullOrWhiteSpace(url) && normalizedWindowTitle.Contains(url, StringComparison.OrdinalIgnoreCase))
            return 20;

        return 0;
    }

    private static string NormalizeBrowserTitle(string? title)
    {
        var normalized = NormalizeTitle(title);
        if (string.IsNullOrWhiteSpace(normalized))
            return normalized;

        string[] suffixes =
        [
            " - Google Chrome",
            " - Microsoft Edge",
            " - Brave",
            " - Opera",
            " - Vivaldi"
        ];

        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return normalized[..^suffix.Length].Trim();
        }

        return normalized;
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
