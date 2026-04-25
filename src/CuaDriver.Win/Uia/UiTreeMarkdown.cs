using System.Text;

namespace CuaDriver.Win.Uia;

internal static class UiTreeMarkdown
{
    public static void AppendElement(StringBuilder sb, UiElementInfo info, int depth, bool actionable)
    {
        var indent = new string(' ', depth * 2);
        var indexText = actionable ? $"[element_index {info.ElementIndex}] " : "";
        var name = string.IsNullOrWhiteSpace(info.Name) ? "" : $" \"{Escape(info.Name)}\"";
        var autoId = string.IsNullOrWhiteSpace(info.AutomationId) ? "" : $" automation_id={Escape(info.AutomationId)}";
        var cls = string.IsNullOrWhiteSpace(info.ClassName) ? "" : $" class={Escape(info.ClassName)}";
        var patterns = info.Patterns.Count == 0 ? "" : $" patterns={string.Join(",", info.Patterns)}";
        var enabled = info.IsEnabled ? "enabled" : "disabled";
        var offscreen = info.IsOffscreen ? " offscreen" : "";
        sb.Append(indent)
          .Append("- ")
          .Append(indexText)
          .Append(info.ControlType)
          .Append(name)
          .Append(' ')
          .Append(enabled)
          .Append(offscreen)
          .Append(" bounds=(").Append(info.Bounds.X).Append(',').Append(info.Bounds.Y).Append(',').Append(info.Bounds.Width).Append(',').Append(info.Bounds.Height).Append(')')
          .Append(autoId)
          .Append(cls)
          .Append(patterns)
          .AppendLine();
    }

    public static string Filter(string markdown, string query)
    {
        var lines = markdown.Split('\n');
        var keep = new SortedSet<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                keep.Add(i);
                var indent = LeadingSpaces(lines[i]);
                for (var j = i - 1; j >= 0; j--)
                {
                    var jIndent = LeadingSpaces(lines[j]);
                    if (jIndent < indent)
                    {
                        keep.Add(j);
                        indent = jIndent;
                    }
                }
            }
        }
        return string.Join('\n', keep.Select(i => lines[i]));
    }

    private static int LeadingSpaces(string s)
    {
        var i = 0;
        while (i < s.Length && s[i] == ' ') i++;
        return i;
    }

    private static string Escape(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();
}
