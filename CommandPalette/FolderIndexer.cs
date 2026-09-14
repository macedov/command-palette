using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CommandPalette;

public static class FolderIndexer
{
    public static Task<List<PaletteItem>> IndexAsync(
        IEnumerable<string> roots,
        IEnumerable<string>? ignoredFolders = null,
        IEnumerable<string>? ignoredFolderNames = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var results = new List<PaletteItem>();

            var ignoredPaths =
                (ignoredFolders ?? [])
                    .Select(SettingsManager.ExpandPath)
                    .Select(NormalizePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var ignoredNames =
                (ignoredFolderNames ?? [])
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var expandedRoot =
                    SettingsManager.ExpandPath(root);

                if (!Directory.Exists(expandedRoot))
                    continue;

                IndexRoot(
                    expandedRoot,
                    results,
                    ignoredPaths,
                    ignoredNames,
                    cancellationToken
                );
            }

            return results;
        }, cancellationToken);
    }

    private static void IndexRoot(
        string root,
        List<PaletteItem> results,
        HashSet<string> ignoredPaths,
        HashSet<string> ignoredNames,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();

        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = stack.Pop();

            if (ShouldIgnore(
                    current,
                    ignoredPaths,
                    ignoredNames))
            {
                continue;
            }

            try
            {
                var name =
                    Path.GetFileName(
                        current.TrimEnd(
                            Path.DirectorySeparatorChar
                        )
                    );

                if (string.IsNullOrWhiteSpace(name))
                    name = current;

                results.Add(
                    new PaletteItem(
                        name,
                        current,
                        PaletteItemType.Folder
                    )
                );

                foreach (var directory in
                         Directory.EnumerateDirectories(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stack.Push(directory);
                }
            }
            catch
            {
                // Folders may become inaccessible while indexing.
            }
        }
    }

    private static bool ShouldIgnore(
        string path,
        HashSet<string> ignoredPaths,
        HashSet<string> ignoredNames)
    {
        var normalizedPath = NormalizePath(path);
        var name = Path.GetFileName(normalizedPath);

        if (ignoredNames.Contains(name))
            return true;

        return ignoredPaths.Any(ignoredPath =>
            normalizedPath.Equals(
                ignoredPath,
                StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(
                ignoredPath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            ));
    }

    private static string NormalizePath(
        string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );
    }
}
