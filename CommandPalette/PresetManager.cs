using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommandPalette;

public enum PresetActionType
{
    OpenApp,
    OpenUrl,
    OpenPath,
    CloseProcess
}

public class PresetAction
{
    public PresetActionType Type { get; set; }

    public string Target { get; set; } = "";
}

public class PresetDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public List<PresetAction> Actions { get; set; } = [];
}

public class PresetConfig
{
    public List<PresetDefinition> Presets { get; set; } = [];
}

public record PresetLoadResult(
    bool Success,
    int Count,
    string? Error
);

public static class PresetManager
{
    private static List<PresetDefinition> _presets = [];

    private static bool _loaded;

    public static string ConfigDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData
            ),
            "CommandPalette"
        );

    public static string ConfigPath { get; } =
        Path.Combine(
            ConfigDirectory,
            "presets.json"
        );

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,

            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

    // ============================================================
    // LOAD / RELOAD
    // ============================================================

    public static PresetLoadResult Reload()
    {
        try
        {
            Directory.CreateDirectory(
                ConfigDirectory
            );

            if (!File.Exists(ConfigPath))
            {
                CreateEmptyConfig();
            }

            Debug.WriteLine(
                $"Loading presets from: {ConfigPath}"
            );

            var json =
                File.ReadAllText(
                    ConfigPath
                );

            var config =
                JsonSerializer.Deserialize<PresetConfig>(
                    json,
                    JsonOptions
                );

            if (config is null)
            {
                throw new InvalidDataException(
                    "presets.json is empty or invalid."
                );
            }

            ValidateConfig(
                config
            );

            _presets =
                config.Presets;

            _loaded = true;

            Debug.WriteLine(
                $"Loaded {_presets.Count} presets."
            );

            return new PresetLoadResult(
                true,
                _presets.Count,
                null
            );
        }
        catch (Exception ex)
        {
            _loaded = true;

            Debug.WriteLine(
                $"Preset reload failed: {ex}"
            );

            return new PresetLoadResult(
                false,
                _presets.Count,
                ex.Message
            );
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        Reload();
    }

    // ============================================================
    // CREATE CONFIG
    // ============================================================

    private static void CreateEmptyConfig()
    {
        var config =
            new PresetConfig();

        var json =
            JsonSerializer.Serialize(
                config,
                JsonOptions
            );

        File.WriteAllText(
            ConfigPath,
            json
        );

        Debug.WriteLine(
            $"Created empty preset config: {ConfigPath}"
        );
    }

    // ============================================================
    // VALIDATION
    // ============================================================

    private static void ValidateConfig(
        PresetConfig config)
    {
        var ids =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var preset
                 in config.Presets)
        {
            if (string.IsNullOrWhiteSpace(
                    preset.Id))
            {
                throw new InvalidDataException(
                    "A preset is missing its Id."
                );
            }

            if (string.IsNullOrWhiteSpace(
                    preset.Name))
            {
                throw new InvalidDataException(
                    $"Preset '{preset.Id}' is missing its Name."
                );
            }

            if (!ids.Add(
                    preset.Id))
            {
                throw new InvalidDataException(
                    $"Duplicate preset Id: '{preset.Id}'."
                );
            }

            foreach (var action
                     in preset.Actions)
            {
                if (string.IsNullOrWhiteSpace(
                        action.Target))
                {
                    throw new InvalidDataException(
                        $"Preset '{preset.Id}' contains " +
                        "an action with no Target."
                    );
                }
            }
        }
    }

    // ============================================================
    // ACCESS
    // ============================================================

    public static IEnumerable<PaletteItem>
        GetPaletteItems()
    {
        EnsureLoaded();

        return _presets.Select(
            preset =>
                new PaletteItem(
                    preset.Name,
                    preset.Id,
                    PaletteItemType.Preset,
                    preset.Description
                )
        );
    }

    public static PresetDefinition?
        GetById(string id)
    {
        EnsureLoaded();

        return _presets.FirstOrDefault(
            preset =>
                preset.Id.Equals(
                    id,
                    StringComparison.OrdinalIgnoreCase
                )
        );
    }
    
    public static List<PresetDefinition> GetAllCopy()
{
    EnsureLoaded();

    return _presets
        .Select(ClonePreset)
        .ToList();
}

public static PresetLoadResult SaveAll(
    IEnumerable<PresetDefinition> presets)
{
    try
    {
        Directory.CreateDirectory(
            ConfigDirectory
        );

        var config =
            new PresetConfig
            {
                Presets =
                    presets
                        .Select(ClonePreset)
                        .ToList()
            };

        ValidateConfig(
            config
        );

        var json =
            JsonSerializer.Serialize(
                config,
                JsonOptions
            );

        // Primeiro escreve em um temporário.
        // Assim reduzimos a chance de deixar
        // o JSON quebrado se algo der errado.
        var tempPath =
            ConfigPath + ".tmp";

        File.WriteAllText(
            tempPath,
            json
        );

        File.Move(
            tempPath,
            ConfigPath,
            true
        );

        _presets =
            config.Presets;

        _loaded = true;

        Debug.WriteLine(
            $"Saved {_presets.Count} presets."
        );

        return new PresetLoadResult(
            true,
            _presets.Count,
            null
        );
    }
    catch (Exception ex)
    {
        Debug.WriteLine(
            $"Preset save failed: {ex}"
        );

        return new PresetLoadResult(
            false,
            _presets.Count,
            ex.Message
        );
    }
}

private static PresetDefinition ClonePreset(
    PresetDefinition preset)
{
    return new PresetDefinition
    {
        Id = preset.Id,
        Name = preset.Name,
        Description = preset.Description,

        Actions =
            preset.Actions
                .Select(action =>
                    new PresetAction
                    {
                        Type = action.Type,
                        Target = action.Target
                    })
                .ToList()
    };
}
}