namespace AutoSub;

public static class SubtitleSplitter
{
    public static List<string> SmartSplit(string text, int maxLineLength = 40)
    {
        // No text, skip processing
        if (string.IsNullOrEmpty(text))
            return [];

        // Nothing to split
        if (text.Length <= maxLineLength)
            return [text];

        // Possible break points
        var delimiters = new[] { ". ", "? ", "! ", ", ", "; ", ": ", " - " };
        foreach (var delim in delimiters)
        {
            var idx = text.LastIndexOf(delim, maxLineLength, StringComparison.Ordinal);
            if (idx > 0)
            {
                var first = text[..(idx + delim.Trim().Length)].Trim();
                var remainder = text[(idx + delim.Length)..].Trim();
                var result = new List<string> { first };
                result.AddRange(SmartSplit(remainder, maxLineLength));
                return result;
            }
        }

        // Fallback: split at nearest space before max length
        var spaceIdx = text.LastIndexOf(' ', maxLineLength);
        if (spaceIdx > 0)
        {
            var first = text[..spaceIdx].Trim();
            var remainder = text[spaceIdx..].Trim();
            var result = new List<string> { first };
            result.AddRange(SmartSplit(remainder, maxLineLength));
            return result;
        }

        // If still nothing, just return whole
        return [text];
    }
}