using System.Globalization;

namespace CuaDriver.Win.Input;

internal static class TextElementSplitter
{
    public static List<string> Split(string text)
    {
        var result = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            result.Add(enumerator.GetTextElement());
        return result;
    }
}
