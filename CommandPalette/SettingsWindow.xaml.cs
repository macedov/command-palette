using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace CommandPalette;

public partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, string?> _applySettings;
    private readonly Func<string?> _beginHotkeyRecording;
    private readonly Action _endHotkeyRecording;

    private readonly ObservableCollection<string> _indexedFolders;
    private readonly ObservableCollection<string> _ignoredFolders;
    private readonly ObservableCollection<string> _ignoredNames;
    private readonly ObservableCollection<CustomApplication> _customApps;
    private readonly ObservableCollection<SearchProviderSettings>
        _searchProviders;
    private bool _isRecordingHotkey;
    private bool _hotkeyWasReleased;
    private readonly bool _onboardingSeen;

    public SettingsWindow(
        AppSettings settings,
        Func<AppSettings, string?> applySettings,
        Func<string?> beginHotkeyRecording,
        Action endHotkeyRecording)
    {
        InitializeComponent();
        WindowSystemMenu.Suppress(this);

        _applySettings = applySettings;
        _beginHotkeyRecording = beginHotkeyRecording;
        _endHotkeyRecording = endHotkeyRecording;
        _onboardingSeen = settings.OnboardingSeen;

        _indexedFolders = [.. settings.IndexedFolders];
        _ignoredFolders = [.. settings.IgnoredFolders];
        _ignoredNames = [.. settings.IgnoredFolderNames];
        _customApps = new ObservableCollection<CustomApplication>(
            settings.CustomApps.Select(app => new CustomApplication
            {
                Name = app.Name,
                Path = app.Path
            })
        );
        _searchProviders =
            new ObservableCollection<SearchProviderSettings>(
                settings.SearchProviders.Select(provider =>
                    new SearchProviderSettings
                    {
                        Name = provider.Name,
                        Prefix = provider.Prefix,
                        SearchUrlTemplate = provider.SearchUrlTemplate,
                        Enabled = provider.Enabled
                    })
            );

        IndexedFoldersList.ItemsSource = _indexedFolders;
        IgnoredFoldersList.ItemsSource = _ignoredFolders;
        IgnoredNamesList.ItemsSource = _ignoredNames;
        CustomAppsGrid.ItemsSource = _customApps;
        SearchProvidersGrid.ItemsSource = _searchProviders;

        HotkeyBox.Text = settings.GlobalHotkey;
        MaximumResultsBox.Text =
            settings.MaximumResults.ToString(CultureInfo.InvariantCulture);
        HideWhenFocusLostBox.IsChecked =
            settings.HideWhenFocusIsLost;
        StartWithWindowsBox.IsChecked = settings.StartWithWindows;
        OpenBindingBox.Text =
            settings.ActionKeybindings[PaletteActionIds.Open];
        CopyBindingBox.Text =
            settings.ActionKeybindings[PaletteActionIds.Copy];
        TerminalBindingBox.Text =
            settings.ActionKeybindings[
                PaletteActionIds.OpenTerminalHere];
        CalculatorEnabledBox.IsChecked = settings.CalculatorEnabled;
        WindowsSettingsEnabledBox.IsChecked =
            settings.WindowsSettingsEnabled;
        SystemCommandsEnabledBox.IsChecked =
            settings.SystemCommandsEnabled;
        ConfirmSystemActionsBox.IsChecked =
            settings.ConfirmDestructiveSystemActions;
    }

    private void HotkeyBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (!_isRecordingHotkey)
            return;

        var key = e.Key == Key.System
            ? e.SystemKey
            : e.Key;

        if (key is Key.LeftAlt or Key.RightAlt or
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        HotkeyBox.Text = SettingsManager.FormatHotkey(
            Keyboard.Modifiers,
            key
        );

        StopHotkeyRecording(waitForKeyRelease: true);
        StatusText.Text = "";
        e.Handled = true;
    }

    private void RecordHotkey_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isRecordingHotkey)
            return;

        var error = _beginHotkeyRecording();

        if (error is not null)
        {
            StatusText.Text = error;
            return;
        }

        _hotkeyWasReleased = true;
        _isRecordingHotkey = true;
        StatusText.Text = "Press the new shortcut.";
        HotkeyBox.Focus();
        HotkeyBox.SelectAll();
    }

    private void StopHotkeyRecording(
        bool waitForKeyRelease = false)
    {
        _isRecordingHotkey = false;

        if (!_hotkeyWasReleased ||
            (waitForKeyRelease &&
             Keyboard.Modifiers != ModifierKeys.None))
        {
            return;
        }

        _hotkeyWasReleased = false;
        _endHotkeyRecording();
    }

    private void AddIndexedFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        AddFolder(_indexedFolders, "Choose a folder to index");
    }

    private void AddIgnoredFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        AddFolder(_ignoredFolders, "Choose a folder to ignore");
    }

    private static void AddFolder(
        ObservableCollection<string> folders,
        string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true ||
            folders.Contains(dialog.FolderName))
        {
            return;
        }

        folders.Add(dialog.FolderName);
    }

    private void RemoveIndexedFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        RemoveSelected(IndexedFoldersList, _indexedFolders);
    }

    private void RemoveIgnoredFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        RemoveSelected(IgnoredFoldersList, _ignoredFolders);
    }

    private void AddIgnoredName_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name = IgnoredNameBox.Text.Trim();

        if (name.Length == 0 || _ignoredNames.Contains(name))
            return;

        _ignoredNames.Add(name);
        IgnoredNameBox.Clear();
    }

    private void RemoveIgnoredName_Click(
        object sender,
        RoutedEventArgs e)
    {
        RemoveSelected(IgnoredNamesList, _ignoredNames);
    }

    private static void RemoveSelected(
        System.Windows.Controls.ListBox list,
        ObservableCollection<string> values)
    {
        if (list.SelectedItem is string selected)
            values.Remove(selected);
    }

    private void AddCustomApp_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an application",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var name = Path.GetFileNameWithoutExtension(dialog.FileName);

        try
        {
            var versionInfo =
                FileVersionInfo.GetVersionInfo(dialog.FileName);

            if (!string.IsNullOrWhiteSpace(versionInfo.ProductName))
            {
                name = versionInfo.ProductName;
            }
            else if (!string.IsNullOrWhiteSpace(
                         versionInfo.FileDescription))
            {
                name = versionInfo.FileDescription;
            }
        }
        catch
        {
            // The executable file name is a safe fallback.
        }

        var app = new CustomApplication
        {
            Name = name,
            Path = dialog.FileName
        };

        _customApps.Add(app);
        CustomAppsGrid.SelectedItem = app;
        CustomAppsGrid.ScrollIntoView(app);
    }

    private void RemoveCustomApp_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CustomAppsGrid.SelectedItem
            is CustomApplication app)
        {
            _customApps.Remove(app);
        }
    }

    private void AddProvider_Click(
        object sender,
        RoutedEventArgs e)
    {
        var provider = new SearchProviderSettings
        {
            Name = "New provider",
            Prefix = "search",
            SearchUrlTemplate =
                "https://example.com/search?q={query}",
            Enabled = true
        };

        _searchProviders.Add(provider);
        SearchProvidersGrid.SelectedItem = provider;
        SearchProvidersGrid.ScrollIntoView(provider);
    }

    private void RemoveProvider_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SearchProvidersGrid.SelectedItem
            is SearchProviderSettings provider)
        {
            _searchProviders.Remove(provider);
        }
    }

    private void RestoreKeybindings_Click(
        object sender,
        RoutedEventArgs e)
    {
        var defaults = PaletteActionIds.CreateDefaultBindings();

        OpenBindingBox.Text = defaults[PaletteActionIds.Open];
        CopyBindingBox.Text = defaults[PaletteActionIds.Copy];
        TerminalBindingBox.Text =
            defaults[PaletteActionIds.OpenTerminalHere];
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        StopHotkeyRecording();

        CustomAppsGrid.CommitEdit(
            System.Windows.Controls.DataGridEditingUnit.Cell,
            true
        );
        SearchProvidersGrid.CommitEdit(
            System.Windows.Controls.DataGridEditingUnit.Cell,
            true
        );
        SearchProvidersGrid.CommitEdit(
            System.Windows.Controls.DataGridEditingUnit.Row,
            true
        );
        CustomAppsGrid.CommitEdit(
            System.Windows.Controls.DataGridEditingUnit.Row,
            true
        );

        if (!int.TryParse(
                MaximumResultsBox.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var maximumResults))
        {
            StatusText.Text = "Maximum results must be a number.";
            return;
        }

        var settings = new AppSettings
        {
            GlobalHotkey = HotkeyBox.Text,
            IndexedFolders = [.. _indexedFolders],
            IgnoredFolders = [.. _ignoredFolders],
            IgnoredFolderNames = [.. _ignoredNames],
            MaximumResults = maximumResults,
            HideWhenFocusIsLost =
                HideWhenFocusLostBox.IsChecked == true,
            StartWithWindows =
                StartWithWindowsBox.IsChecked == true,
            CustomApps = [.. _customApps],
            ActionKeybindings = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                [PaletteActionIds.Open] = OpenBindingBox.Text,
                [PaletteActionIds.Copy] = CopyBindingBox.Text,
                [PaletteActionIds.OpenTerminalHere] =
                    TerminalBindingBox.Text
            },
            SearchProviders = [.. _searchProviders],
            CalculatorEnabled =
                CalculatorEnabledBox.IsChecked == true,
            WindowsSettingsEnabled =
                WindowsSettingsEnabledBox.IsChecked == true,
            SystemCommandsEnabled =
                SystemCommandsEnabledBox.IsChecked == true,
            ConfirmDestructiveSystemActions =
                ConfirmSystemActionsBox.IsChecked == true,
            OnboardingSeen = _onboardingSeen
        };

        if (!SettingsManager.TryValidate(settings, out var error))
        {
            StatusText.Text = error;
            return;
        }

        error = _applySettings(settings);

        if (error is not null)
        {
            StatusText.Text = error;
            return;
        }

        Close();
    }

    private void Cancel_Click(
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

        StopHotkeyRecording();
        Close();
        e.Handled = true;
    }

    private void Window_PreviewKeyUp(
        object sender,
        KeyEventArgs e)
    {
        if (!_isRecordingHotkey &&
            Keyboard.Modifiers == ModifierKeys.None)
        {
            StopHotkeyRecording();
        }
    }

    private void Window_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (!GeneralScrollViewer.IsMouseOver)
            return;

        for (var step = 0; step < 3; step++)
        {
            if (e.Delta > 0)
                GeneralScrollViewer.LineUp();
            else
                GeneralScrollViewer.LineDown();
        }

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

    protected override void OnClosed(
        EventArgs e)
    {
        StopHotkeyRecording();
        base.OnClosed(e);
    }
}
