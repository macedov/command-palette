using System;
using System.Collections.Generic;

namespace CommandPalette;

public static class WebSearch
{
    private static readonly Dictionary<string, SearchEngine> Engines =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["y"] = new SearchEngine(
                "YouTube",
                "https://www.youtube.com/results?search_query={0}"
            ),

            ["g"] = new SearchEngine(
                "Google",
                "https://www.google.com/search?q={0}"
            ),

            ["gh"] = new SearchEngine(
                "GitHub",
                "https://github.com/search?q={0}"
            ),

            ["r"] = new SearchEngine(
                "Reddit",
                "https://www.reddit.com/search/?q={0}"
            )
        };

    public static PaletteItem? TryCreateResult(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var firstSpace = input.IndexOf(' ');

        if (firstSpace <= 0)
            return null;

        var prefix = input[..firstSpace].Trim();
        var query = input[(firstSpace + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(query))
            return null;

        if (!Engines.TryGetValue(prefix, out var engine))
            return null;

        var encodedQuery = Uri.EscapeDataString(query);

        var url = string.Format(
            engine.UrlFormat,
            encodedQuery
        );

        return new PaletteItem(
            $"Search {engine.Name}: {query}",
            url,
            PaletteItemType.WebSearch,
            $"{engine.Name} Search"
        );
    }

    public static PaletteItem? TryCreateHint(string input)
    {
        input = input.Trim();

        if (!Engines.TryGetValue(input, out var engine))
            return null;

        return new PaletteItem(
            $"{engine.Name} Search",
            $"{input} <query>",
            PaletteItemType.Hint,
            $"{input} <query>"
        );
    }
}

public class SearchEngine
{
    public string Name { get; }
    public string UrlFormat { get; }

    public SearchEngine(
        string name,
        string urlFormat)
    {
        Name = name;
        UrlFormat = urlFormat;
    }
}