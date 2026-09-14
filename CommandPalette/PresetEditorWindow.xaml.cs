using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace CommandPalette;

public partial class PresetEditorWindow : Window
{
    public sealed record ActionTypeOption(
        PresetActionType Value,
        string Label
    );

    public static IReadOnlyList<ActionTypeOption> ActionTypes { get; } =
    [
        new(PresetActionType.OpenApp, "Open application"),
        new(PresetActionType.OpenPath, "Open folder"),
        new(PresetActionType.OpenUrl, "Open URL"),
        new(PresetActionType.CloseApp, "Close application"),
        new(PresetActionType.CloseAppWindows, "Close application windows"),
        new(PresetActionType.CloseProcess, "Close process (advanced)")
    ];

    private List<PresetDefinition> _presets = [];

    private PresetDefinition? _currentPreset;

    private bool _changingSelection;

    public IReadOnlyList<PaletteItem> KnownApps { get; }

    public PresetEditorWindow(
        IEnumerable<PaletteItem>? knownApps = null)
    {
        KnownApps =
            (knownApps ?? [])
                .Where(item => item.Type == PaletteItemType.App)
                .OrderBy(
                    item => item.Name,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        InitializeComponent();
        ApplicationIconProvider.ApplyTo(this);
        WindowSystemMenu.Suppress(this);

        LoadPresets();
    }

    private void LoadPresets()
    {
        var result =
            PresetManager.Reload();

        if (!result.Success)
        {
            MessageBox.Show(
                result.Error,
                "Failed to load presets"
            );

            return;
        }

        _presets =
            PresetManager.GetAllCopy();

        RefreshPresetList();

        if (_presets.Count > 0)
        {
            PresetList.SelectedIndex = 0;
        }
        else
        {
            LoadPreset(
                null
            );
        }
    }

    private void RefreshPresetList(
        PresetDefinition? selected = null)
    {
        _changingSelection = true;

        PresetList.ItemsSource = null;

        PresetList.ItemsSource =
            _presets;

        if (selected is not null)
        {
            PresetList.SelectedItem =
                selected;
        }

        _changingSelection = false;
    }

    private void PresetList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_changingSelection)
            return;

        SaveCurrentFields();

        _currentPreset =
            PresetList.SelectedItem
                as PresetDefinition;

        LoadPreset(
            _currentPreset
        );
    }

    private void LoadPreset(
        PresetDefinition? preset)
    {
        _currentPreset =
            preset;

        var enabled =
            preset is not null;

        EditorPanel.IsEnabled =
            enabled;

        if (preset is null)
        {
            IdBox.Text = "";
            NameBox.Text = "";
            DescriptionBox.Text = "";

            ActionsGrid.ItemsSource =
                null;

            return;
        }

        IdBox.Text =
            preset.Id;

        NameBox.Text =
            preset.Name;

        DescriptionBox.Text =
            preset.Description;

        RefreshActions();
    }

    private void SaveCurrentFields()
    {
        if (_currentPreset is null)
            return;

        CommitActionEdit();

        _currentPreset.Id =
            IdBox.Text.Trim();

        _currentPreset.Name =
            NameBox.Text.Trim();

        _currentPreset.Description =
            DescriptionBox.Text.Trim();
    }

    private void CommitActionEdit()
    {
        ActionsGrid.CommitEdit(
            DataGridEditingUnit.Cell,
            true
        );

        ActionsGrid.CommitEdit(
            DataGridEditingUnit.Row,
            true
        );
    }

    private void NewPreset_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveCurrentFields();

        var preset =
            new PresetDefinition
            {
                Id = GeneratePresetId(),

                Name = "New Preset",

                Description = "",

                Actions = []
            };

        _presets.Add(
            preset
        );

        RefreshPresetList(
            preset
        );

        LoadPreset(
            preset
        );

        NameBox.Focus();

        NameBox.SelectAll();
    }

    private string GeneratePresetId()
    {
        var baseId =
            "new-preset";

        var id =
            baseId;

        var number = 2;

        while (_presets.Any(
                   preset =>
                       preset.Id.Equals(
                           id,
                           StringComparison.OrdinalIgnoreCase
                       )))
        {
            id =
                $"{baseId}-{number}";

            number++;
        }

        return id;
    }

    private void DeletePreset_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentPreset is null)
            return;

        var result =
            MessageBox.Show(
                $"Delete preset '{_currentPreset.Name}'?",
                "Delete preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

        if (result !=
            MessageBoxResult.Yes)
        {
            return;
        }

        var index =
            _presets.IndexOf(
                _currentPreset
            );

        _presets.Remove(
            _currentPreset
        );

        _currentPreset =
            null;

        RefreshPresetList();

        if (_presets.Count == 0)
        {
            LoadPreset(
                null
            );

            return;
        }

        if (index >= _presets.Count)
        {
            index =
                _presets.Count - 1;
        }

        PresetList.SelectedIndex =
            index;
    }

    private void RefreshActions()
    {
        ActionsGrid.ItemsSource =
            null;

        ActionsGrid.ItemsSource =
            _currentPreset?.Actions;
    }

    private void AddAction_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentPreset is null)
            return;

        SaveCurrentFields();

        var action =
            new PresetAction
            {
                Type =
                    PresetActionType.OpenApp,

                Target = ""
            };

        _currentPreset.Actions.Add(
            action
        );

        RefreshActions();

        ActionsGrid.SelectedItem =
            action;

        ActionsGrid.ScrollIntoView(
            action
        );
    }

    private void DeleteAction_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentPreset is null)
            return;

        if (ActionsGrid.SelectedItem
            is not PresetAction action)
        {
            return;
        }

        _currentPreset.Actions.Remove(
            action
        );

        RefreshActions();
    }

    private void BrowseActionPath_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: PresetAction action
            })
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            action.Target = dialog.FolderName;
        }
    }

    private void MoveActionUp_Click(
        object sender,
        RoutedEventArgs e)
    {
        MoveSelectedAction(-1);
    }

    private void MoveActionDown_Click(
        object sender,
        RoutedEventArgs e)
    {
        MoveSelectedAction(1);
    }

    private void MoveSelectedAction(
        int offset)
    {
        if (_currentPreset is null ||
            ActionsGrid.SelectedItem is not PresetAction action)
        {
            return;
        }

        CommitActionEdit();

        var currentIndex =
            _currentPreset.Actions.IndexOf(action);
        var destinationIndex = currentIndex + offset;

        if (destinationIndex < 0 ||
            destinationIndex >= _currentPreset.Actions.Count)
        {
            return;
        }

        _currentPreset.Actions.RemoveAt(currentIndex);
        _currentPreset.Actions.Insert(destinationIndex, action);

        RefreshActions();
        ActionsGrid.SelectedItem = action;
        ActionsGrid.ScrollIntoView(action);
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveCurrentFields();

        var result =
            PresetManager.SaveAll(
                _presets
            );

        if (!result.Success)
        {
            StatusText.Text =
                $"⚠ {result.Error}";

            MessageBox.Show(
                result.Error,
                "Couldn't save presets"
            );

            return;
        }

        RefreshPresetList(
            _currentPreset
        );

        StatusText.Text =
            $"✓ Saved {result.Count} presets";
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed &&
            e.GetPosition(this).X < ActualWidth - 60)
        {
            DragMove();
        }
    }
}
