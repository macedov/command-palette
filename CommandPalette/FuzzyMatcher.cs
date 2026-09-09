using System;

namespace CommandPalette;

public static class FuzzyMatcher
{
    public static int Score(
        string text,
        string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 1;

        text = text.ToLowerInvariant();
        query = query.ToLowerInvariant();

        if (text == query)
            return 1000;

        if (text.StartsWith(query))
            return 800;

        if (text.Contains(query))
            return 600;

        var score = 0;
        var queryIndex = 0;
        var consecutive = 0;

        for (var i = 0;
             i < text.Length && queryIndex < query.Length;
             i++)
        {
            if (text[i] != query[queryIndex])
            {
                consecutive = 0;
                continue;
            }

            queryIndex++;

            consecutive++;

            score += 10;

            // Letras juntas são melhores.
            score += consecutive * 5;
        }

        if (queryIndex != query.Length)
            return 0;

        return score;
    }
}