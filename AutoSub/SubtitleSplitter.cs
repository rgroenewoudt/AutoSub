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
                return
                [
                    text[..(idx + delim.Trim().Length)].Trim(),
                    text[(idx + delim.Length)..].Trim()
                ];
        }

        // Fallback: split at nearest space before max length
        var spaceIdx = text.LastIndexOf(' ', maxLineLength);
        if (spaceIdx > 0)
        {
            return
            [
                text[..spaceIdx].Trim(),
                text[spaceIdx..].Trim()
            ];
        }

        // If still nothing, just return whole
        return [text];
    }
}