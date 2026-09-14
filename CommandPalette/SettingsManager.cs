using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;

namespace CommandPalette;

public sealed class AppSettings
{
    public string GlobalHotkey { get; set; } = "Alt+Space";

    public List<string> IndexedFolders { get; set; } =
    [
        "%USERPROFILE%"
    ];

    public List<string> IgnoredFolders { get; set; } = [];

    public List<string> IgnoredFolderNames { get; set; } = [];

    public int MaximumResults { get; set; } = 30;

    public bool HideWhenFocusIsLost { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public List<CustomApplication> CustomApps { get; set; } = [];

    public Dictionary<string, string> ActionKeybindings { get; set; } =
        PaletteActionIds.CreateDefaultBindings();

    public List<SearchProviderSettings> SearchProviders { get; set; } =
        SearchProviderSettings.CreateDefaults();

    public bool CalculatorEnabled { get; set; } = true;

    public bool WindowsSettingsEnabled { get; set; } = true;

    public bool SystemCommandsEnabled { get; set; } = true;

    public bool ConfirmDestructiveSystemActions { get; set; } = true;

    public bool OnboardingSeen { get; set; }
}

public sealed class CustomApplication
{
    public string Name { get; set; } = "";

    public string Path { get; set; } = "";
}

public sealed class SearchProviderSettings
{
    public string Name { get; set; } = "";

    public string Prefix { get; set; } = "";

    public string SearchUrlTemplate { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public static List<SearchProviderSettings> CreateDefaults()
    {
        return
        [
            new SearchProviderSettings
            {
                Name = "Google",
                Prefix = "g",
                SearchUrlTemplate =
                    "https://www.google.com/search?q={query}",
                Enabled = true
            },
            new SearchProviderSettings
            {
                Name = "YouTube",
                Prefix = "y",
                SearchUrlTemplate =
                    "https://www.youtube.com/results?search_query={query}",
                Enabled = true
            }
        ];
    }

    public static List<SearchProviderSettings> CreateLegacyDefaults()
    {
        var providers = CreateDefaults();

        providers.Add(
            new SearchProviderSettings
            {
                Name = "GitHub",
                Prefix = "gh",
                SearchUrlTemplate =
                    "https://github.com/search?q={query}",
                Enabled = true
            }
        );
        providers.Add(
            new SearchProviderSettings
            {
                Name = "Reddit",
                Prefix = "r",
                SearchUrlTemplate =
                    "https://www.reddit.com/search/?q={query}",
                Enabled = true
            }
        );

        return providers;
    }
}

public readonly record struct HotkeyGesture(
    ModifierKeys Modifiers,
    Key Key
);

public record SettingsLoadResult(
    AppSettings Settings,
    bool Success,
    string? Error
);

public record SettingsSaveResult(
    bool Success,
    string? Error
);

public static class SettingsManager
{
    public static string ConfigDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData
            ),
            "CommandPalette"
        );

    public static string ConfigPath { get; } =
        Path.Combine(ConfigDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public static SettingsLoadResult Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);

            if (!File.Exists(ConfigPath))
            {
                var defaults = CreateDefaults();
                var saveResult = Save(defaults);

                if (!saveResult.Success)
                {
                    return new SettingsLoadResult(
                        defaults,
                        false,
                        saveResult.Error
                    );
                }
            }

            var json = File.ReadAllText(ConfigPath);
            var hadSearchProviders =
                HasJsonProperty(json, "SearchProviders");

            var settings =
                JsonSerializer.Deserialize<AppSettings>(
                    json,
                    JsonOptions
                ) ?? throw new InvalidDataException(
                    "settings.json is empty or invalid."
                );

            if (!hadSearchProviders)
            {
                settings.SearchProviders =
                    SearchProviderSettings.CreateLegacyDefaults();
            }

            Normalize(settings);

            if (!TryValidate(settings, out var error))
            {
                throw new InvalidDataException(error);
            }

            if (!hadSearchProviders)
            {
                _ = Save(settings);
            }

            return new SettingsLoadResult(
                settings,
                true,
                null
            );
        }
        catch (Exception ex)
        {
            return new SettingsLoadResult(
                CreateDefaults(),
                false,
                ConfigFileRecovery.DescribeFailure(ConfigPath, ex)
            );
        }
    }

    public static SettingsSaveResult Save(
        AppSettings settings)
    {
        try
        {
            var copy = Clone(settings);
            Normalize(copy);

            if (!TryValidate(copy, out var error))
            {
                return new SettingsSaveResult(false, error);
            }

            Directory.CreateDirectory(ConfigDirectory);

            var tempPath = ConfigPath + ".tmp";

            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(copy, JsonOptions)
            );

            File.Move(tempPath, ConfigPath, true);

            return new SettingsSaveResult(true, null);
        }
        catch (Exception ex)
        {
            return new SettingsSaveResult(false, ex.Message);
        }
    }

    public static AppSettings CreateDefaults()
    {
        return new AppSettings();
    }

    public static AppSettings Clone(
        AppSettings settings)
    {
        return new AppSettings
        {
            GlobalHotkey = settings.GlobalHotkey,
            IndexedFolders = [.. settings.IndexedFolders],
            IgnoredFolders = [.. settings.IgnoredFolders],
            IgnoredFolderNames = [.. settings.IgnoredFolderNames],
            MaximumResults = settings.MaximumResults,
            HideWhenFocusIsLost = settings.HideWhenFocusIsLost,
            StartWithWindows = settings.StartWithWindows,
            CustomApps = settings.CustomApps
                .Select(app => new CustomApplication
                {
                    Name = app.Name ?? "",
                    Path = app.Path ?? ""
                })
                .ToList(),
            ActionKeybindings = new Dictionary<string, string>(
                settings.ActionKeybindings,
                StringComparer.OrdinalIgnoreCase
            ),
            SearchProviders = settings.SearchProviders
                .Select(provider => new SearchProviderSettings
                {
                    Name = provider.Name,
                    Prefix = provider.Prefix,
                    SearchUrlTemplate = provider.SearchUrlTemplate,
                    Enabled = provider.Enabled
                })
                .ToList(),
            CalculatorEnabled = settings.CalculatorEnabled,
            WindowsSettingsEnabled = settings.WindowsSettingsEnabled,
            SystemCommandsEnabled = settings.SystemCommandsEnabled,
            ConfirmDestructiveSystemActions =
                settings.ConfirmDestructiveSystemActions,
            OnboardingSeen = settings.OnboardingSeen
        };
    }

    public static AppSettings NormalizeCopy(
        AppSettings settings)
    {
        var copy = Clone(settings);
        Normalize(copy);
        return copy;
    }

    public static bool TryValidate(
        AppSettings settings,
        out string? error)
    {
        if (!TryParseHotkey(
                settings.GlobalHotkey,
                out var globalHotkey,
                out error))
        {
            return false;
        }

        if (settings.MaximumResults is < 1 or > 100)
        {
            error =
                "Maximum results must be between 1 and 100.";

            return false;
        }

        if (settings.IndexedFolders.Count == 0)
        {
            error = "Add at least one indexed folder.";
            return false;
        }

        foreach (var path in settings.IndexedFolders
                     .Concat(settings.IgnoredFolders))
        {
            try
            {
                var expandedPath = ExpandPath(path);

                if (string.IsNullOrWhiteSpace(path) ||
                    !Path.IsPathFullyQualified(expandedPath))
                {
                    throw new ArgumentException();
                }

                _ = Path.GetFullPath(expandedPath);
            }
            catch
            {
                error = $"Folder path is not valid: '{path}'.";
                return false;
            }
        }

        if (settings.IgnoredFolderNames.Any(name =>
                string.IsNullOrWhiteSpace(name) ||
                name.IndexOfAny(
                    [
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    ]) >= 0))
        {
            error =
                "Ignored folder names must be simple names, not paths.";
            return false;
        }

        var customAppNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var app in settings.CustomApps)
        {
            var path = ExpandPath(app.Path ?? "");

            if (string.IsNullOrWhiteSpace(app.Name) ||
                !customAppNames.Add(app.Name.Trim()))
            {
                error =
                    "Custom applications need unique, non-empty names.";

                return false;
            }

            if (!Path.IsPathFullyQualified(path) ||
                !Path.GetExtension(path).Equals(
                    ".exe",
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                error =
                    $"Custom application path is not a valid .exe: '{app.Path}'.";

                return false;
            }
        }

        var usedBindings = new HashSet<HotkeyGesture>();

        foreach (var actionId in PaletteActionIds
                     .CreateDefaultBindings()
                     .Keys)
        {
            if (!settings.ActionKeybindings.TryGetValue(
                    actionId,
                    out var binding) ||
                !TryParseKeybinding(
                    binding,
                    out var gesture,
                    out error))
            {
                error ??= $"Missing keybinding for '{actionId}'.";
                return false;
            }

            if (!usedBindings.Add(gesture))
            {
                error = $"Keybinding conflict: {binding}.";
                return false;
            }

            if (gesture == globalHotkey)
            {
                error =
                    $"Keybinding {binding} conflicts with the global hotkey.";

                return false;
            }

            if (gesture.Modifiers == ModifierKeys.None &&
                gesture.Key is Key.Up or Key.Down or
                    Key.Escape or Key.F5)
            {
                error = $"Keybinding {binding} is reserved by the palette.";
                return false;
            }
        }

        var prefixes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var provider in settings.SearchProviders)
        {
            if (string.IsNullOrWhiteSpace(provider.Name) ||
                string.IsNullOrWhiteSpace(provider.Prefix) ||
                provider.Prefix.Any(char.IsWhiteSpace))
            {
                error =
                    "Search providers need a name and a prefix without spaces.";

                return false;
            }

            if (!prefixes.Add(provider.Prefix.Trim()))
            {
                error =
                    $"Duplicate search provider prefix: '{provider.Prefix}'.";

                return false;
            }

            if (!provider.SearchUrlTemplate.Contains(
                    "{query}",
                    StringComparison.Ordinal))
            {
                error =
                    $"Provider '{provider.Name}' must include {{query}} in its URL template.";

                return false;
            }

            var sampleUrl = provider.SearchUrlTemplate.Replace(
                "{query}",
                "test",
                StringComparison.Ordinal
            );

            if (!Uri.TryCreate(sampleUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                error =
                    $"Provider '{provider.Name}' has an invalid URL template.";

                return false;
            }
        }

        error = null;
        return true;
    }

    public static bool TryParseHotkey(
        string text,
        out HotkeyGesture hotkey,
        out string? error)
    {
        return TryParseGesture(
            text,
            requireModifier: true,
            out hotkey,
            out error
        );
    }

    public static bool TryParseKeybinding(
        string text,
        out HotkeyGesture keybinding,
        out string? error)
    {
        return TryParseGesture(
            text,
            requireModifier: false,
            out keybinding,
            out error
        );
    }

    private static bool TryParseGesture(
        string text,
        bool requireModifier,
        out HotkeyGesture hotkey,
        out string? error)
    {
        hotkey = default;

        var parts = text.Split(
            '+',
            StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries
        );

        var modifiers = ModifierKeys.None;
        Key? key = null;

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Control;
            }
            else if (part.Equals(
                         "Alt",
                         StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Alt;
            }
            else if (part.Equals(
                         "Shift",
                         StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Shift;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals(
                         "Windows",
                         StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Windows;
            }
            else if (key is null &&
                     Enum.TryParse<Key>(part, true, out var parsedKey) &&
                     !IsModifierKey(parsedKey) &&
                     parsedKey != Key.None)
            {
                key = parsedKey;
            }
            else
            {
                error = $"Invalid hotkey component: '{part}'.";
                return false;
            }
        }

        if ((requireModifier && modifiers == ModifierKeys.None) ||
            key is null)
        {
            error =
                requireModifier
                    ? "The hotkey must contain a modifier and a key, for example Alt+Space."
                    : "The keybinding must contain a key.";

            return false;
        }

        hotkey = new HotkeyGesture(modifiers, key.Value);
        error = null;
        return true;
    }

    public static string FormatHotkey(
        ModifierKeys modifiers,
        Key key)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");

        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");

        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");

        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        parts.Add(key.ToString());

        return string.Join('+', parts);
    }

    public static string ExpandPath(
        string path)
    {
        return Environment.ExpandEnvironmentVariables(
            path.Trim().Trim('"')
        );
    }

    private static void Normalize(
        AppSettings settings)
    {
        settings.GlobalHotkey = settings.GlobalHotkey.Trim();
        settings.IndexedFolders = NormalizeList(settings.IndexedFolders);
        settings.IgnoredFolders = NormalizeList(settings.IgnoredFolders);
        settings.IgnoredFolderNames =
            NormalizeList(settings.IgnoredFolderNames);
        settings.CustomApps = settings.CustomApps?
            .Where(app => app is not null)
            .Select(app => new CustomApplication
            {
                Name = (app.Name ?? "").Trim(),
                Path = (app.Path ?? "").Trim()
            })
            .ToList() ?? [];

        var defaultBindings =
            PaletteActionIds.CreateDefaultBindings();

        if (settings.ActionKeybindings is not null)
        {
            foreach (var (actionId, binding) in
                     settings.ActionKeybindings)
            {
                if (defaultBindings.ContainsKey(actionId) &&
                    !string.IsNullOrWhiteSpace(binding))
                {
                    defaultBindings[actionId] = binding.Trim();
                }
            }
        }

        settings.ActionKeybindings = defaultBindings;
        settings.SearchProviders = settings.SearchProviders?
            .Where(provider => provider is not null)
            .Select(provider => new SearchProviderSettings
            {
                Name = (provider.Name ?? "").Trim(),
                Prefix = (provider.Prefix ?? "").Trim(),
                SearchUrlTemplate =
                    (provider.SearchUrlTemplate ?? "").Trim(),
                Enabled = provider.Enabled
            })
            .ToList() ?? SearchProviderSettings.CreateDefaults();
    }

    private static List<string> NormalizeList(
        IEnumerable<string>? values)
    {
        return values?
                   .Where(value => value is not null)
                   .Select(value => value.Trim())
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList() ?? [];
    }

    private static bool IsModifierKey(
        Key key)
    {
        return key is Key.LeftAlt or Key.RightAlt or
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin;
    }

    private static bool HasJsonProperty(
        string json,
        string propertyName)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.ValueKind == JsonValueKind.Object &&
               document.RootElement
                   .EnumerateObject()
                   .Any(property => property.Name.Equals(
                       propertyName,
                       StringComparison.OrdinalIgnoreCase
                   ));
    }
}
