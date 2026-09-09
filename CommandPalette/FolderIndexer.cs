using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CommandPalette;

public static class FolderIndexer
{
    public static Task<List<PaletteItem>> IndexAsync(
        IEnumerable<string> roots)
    {
        return Task.Run(() =>
        {
            var results = new List<PaletteItem>();

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                IndexRoot(root, results);
            }

            return results;
        });
    }

    private static void IndexRoot(
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
                    stack.Push(directory);
                }
            }
            catch
            {
                // Algumas pastas do Windows dão Access Denied.
                // Só ignoramos e continuamos.
            }
        }
    }
}