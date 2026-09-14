using System;
using System.Collections.Generic;
using System.Linq;

namespace CommandPalette;

public static class WebSearch
{
    public static PaletteItem? TryCreateResult(
        string input,
        IEnumerable<SearchProviderSettings> providers)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var firstSpace = input.IndexOf(' ');

        if (firstSpace <= 0)
            return null;

        var prefix = input[..firstSpace].Trim();
        var query = input[(firstSpace + 1)..].Trim();

        if (query.Length == 0)
            return null;

        var provider = providers.FirstOrDefault(item =>
            item.Enabled &&
            item.Prefix.Equals(
                prefix,
                StringComparison.OrdinalIgnoreCase
            ));

        if (provider is null)
            return null;

        var url = provider.SearchUrlTemplate.Replace(
            "{query}",
            Uri.EscapeDataString(query),
            StringComparison.Ordinal
        );

        return new PaletteItem(
            $"Search {provider.Name}: {query}",
            url,
            PaletteItemType.WebSearch,
            $"{provider.Name} Search"
        );
    }

    public static PaletteItem? TryCreateHint(
        string input,
        IEnumerable<SearchProviderSettings> providers)
    {
        input = input.Trim();

        var provider = providers.FirstOrDefault(item =>
            item.Enabled &&
            item.Prefix.Equals(
                input,
                StringComparison.OrdinalIgnoreCase
            ));

        if (provider is null)
            return null;

        return new PaletteItem(
            $"{provider.Name} Search",
            $"{input} <query>",
            PaletteItemType.Hint,
            $"{input} <query>"
        );
    }
}
