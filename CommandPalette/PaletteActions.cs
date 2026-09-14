using System;
using System.Collections.Generic;

namespace CommandPalette;

public static class PaletteActionIds
{
    public const string Open = "open";
    public const string Copy = "copy";
    public const string OpenTerminalHere = "open-terminal-here";

    public static Dictionary<string, string> CreateDefaultBindings()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Open] = "Enter",
            [Copy] = "Ctrl+C",
            [OpenTerminalHere] = "Ctrl+Enter"
        };
    }
}

public sealed record PaletteActionDescriptor(
    string Id,
    string Label
);

public static class PaletteActionCatalog
{
    public static IReadOnlyList<PaletteActionDescriptor> GetActions(
        PaletteItem item)
    {
        return item.Type switch
        {
            PaletteItemType.Folder =>
            [
                new(PaletteActionIds.Open, "Open"),
                new(PaletteActionIds.OpenTerminalHere, "Terminal"),
                new(PaletteActionIds.Copy, "Copy path")
            ],

            PaletteItemType.App => HasUsefulTarget(item.Path)
                ?
                [
                    new(PaletteActionIds.Open, "Open"),
                    new(PaletteActionIds.Copy, "Copy target")
                ]
                :
                [
                    new(PaletteActionIds.Open, "Open")
                ],

            PaletteItemType.WebSearch or
            PaletteItemType.Url =>
            [
                new(PaletteActionIds.Open, "Open"),
                new(PaletteActionIds.Copy, "Copy URL")
            ],

            PaletteItemType.Calculator =>
            [
                new(PaletteActionIds.Open, "Copy result"),
                new(PaletteActionIds.Copy, "Copy result")
            ],

            PaletteItemType.Preset =>
            [
                new(PaletteActionIds.Open, "Run")
            ],

            PaletteItemType.Command or
            PaletteItemType.WindowsSetting or
            PaletteItemType.SystemCommand =>
            [
                new(PaletteActionIds.Open, "Run")
            ],

            _ => []
        };
    }

    public static bool Supports(
        PaletteItem item,
        string actionId)
    {
        return GetActions(item).Any(action =>
            action.Id.Equals(
                actionId,
                StringComparison.OrdinalIgnoreCase
            ));
    }

    public static string? GetCopyText(
        PaletteItem item)
    {
        return item.Type switch
        {
            PaletteItemType.Folder or
            PaletteItemType.WebSearch or
            PaletteItemType.Url or
            PaletteItemType.Calculator => item.Path,

            PaletteItemType.App when HasUsefulTarget(item.Path) =>
                item.Path,

            _ => null
        };
    }

    private static bool HasUsefulTarget(
        string target)
    {
        return !string.IsNullOrWhiteSpace(target) &&
               !target.StartsWith(
                   AppIndexer.ShellAppPrefix,
                   StringComparison.OrdinalIgnoreCase) &&
               !target.StartsWith(
                   AppIndexer.ShellPathPrefix,
                   StringComparison.OrdinalIgnoreCase);
    }
}
