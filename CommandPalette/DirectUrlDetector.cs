using System;

namespace CommandPalette;

public static class DirectUrlDetector
{
    public static PaletteItem? TryCreateResult(
        string input)
    {
        input = input.Trim();

        if (input.Length == 0 ||
            input.Any(char.IsWhiteSpace) ||
            input.Contains('\\') ||
            input.StartsWith('/') ||
            input.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var candidate = input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? input
            : $"https://{input}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.Host.Contains('.') ||
            uri.Host.EndsWith('.'))
        {
            return null;
        }

        return new PaletteItem(
            $"Open {input}",
            uri.AbsoluteUri,
            PaletteItemType.Url,
            "URL"
        );
    }
}
