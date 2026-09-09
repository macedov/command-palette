using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CommandPalette;

public static class AppIndexer
{
    public static Task<List<PaletteItem>> IndexAsync()
    {
        return Task.Run(() =>
        {
            var results = new List<PaletteItem>();

            var programFolders = new[]
            {
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Programs),

                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonPrograms)
            };

            foreach (var folder in programFolders)
            {
                if (string.IsNullOrWhiteSpace(folder))
                    continue;

                if (!Directory.Exists(folder))
                    continue;

                Debug.WriteLine($"Scanning apps: {folder}");

                IndexFolder(folder, results);
            }

            var finalResults = results
                .GroupBy(
                    item => item.Path,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            Debug.WriteLine(
                $"AppIndexer found {finalResults.Count} apps."
            );

            return finalResults;
        });
    }

    private static void IndexFolder(
        string root,
        List<PaletteItem> results)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            try
            {
                foreach (var file in
                         Directory.EnumerateFiles(current))
                {
                    var extension =
                        Path.GetExtension(file);

                    if (!extension.Equals(
                            ".lnk",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name =
                        Path.GetFileNameWithoutExtension(file);

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    results.Add(
                        new PaletteItem(
                            name,
                            file,
                            PaletteItemType.App,
                            "Application"
                        )
                    );
                }
            }
            catch
            {
                // Ignora arquivos inacessíveis.
            }

            try
            {
                foreach (var directory in
                         Directory.EnumerateDirectories(current))
                {
                    stack.Push(directory);
                }
            }
            catch
            {
                // Ignora diretórios inacessíveis.
            }
        }
    }
}