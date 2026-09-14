using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CommandPalette;

public partial class MainWindow : Window
{
    private const int PrimaryHotkeyId = 9000;
    private const int SecondaryHotkeyId = 9001;

    private const uint ModShift = 0x0004;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private const int WmHotkey = 0x0312;
    private const int WmSysCommand = 0x0112;
    private const int ScKeyMenu = 0xF100;

    private HwndSource? _source;
    private int _activeHotkeyId;
    private bool _isHotkeySuspended;

    private AppSettings _settings;
    private readonly string? _settingsLoadError;

    private CancellationTokenSource? _folderIndexCancellation;
    private int _folderIndexGeneration;

    private static readonly List<PaletteItem> BuiltInCommands =
    [
        new PaletteItem(
            "Settings",
            "settings",
            PaletteItemType.Command,
            "Configure Command Palette"
        ),
        new PaletteItem(
            "Presets",
            "presets",
            PaletteItemType.Command,
            "Manage multi-action commands"
        ),
        new PaletteItem(
            "Help",
            "help",
            PaletteItemType.Command,
            "Shortcuts, examples and available features"
        ),
        new PaletteItem(
            "Exit Command Palette",
            "exit",
            PaletteItemType.Command,
            "Quit and stop the application completely",
            "exit quit close command palette"
        )
    ];

    // Keep indexes separate so refreshing apps does not discard folders.
    private List<PaletteItem> _apps = [];
    private List<PaletteItem> _folders = [];

    private List<PaletteItem> _items = [];

    private bool _isQuickRefreshing;
    private bool _isFolderIndexing;

    private const string DefaultStatus =
        "F5 Refresh    Esc Close";

    private int _statusRevision;
    private string? _armedSystemCommandId;
    private DateTime _confirmationExpiresAt;
    private SettingsWindow? _settingsWindow;
    private PresetEditorWindow? _presetEditorWindow;
    private HelpWindow? _helpWindow;
    private SystemTrayService? _trayService;
    private bool _isShuttingDown;

    public MainWindow()
    {
        var settingsResult = SettingsManager.Load();

        _settings = settingsResult.Settings;
        _settingsLoadError = settingsResult.Error;

        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        Deactivated += OnDeactivated;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    private async void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Hide();

        // Load lightweight sources before starting folder traversal.
        await RefreshQuickAsync();

        StartFolderIndex();

        InitializeSystemTray();

        if (_settingsLoadError is not null)
        {
            ShowTemporaryStatus(
                $"⚠ settings.json: {_settingsLoadError}",
                5000
            );
        }
        else if (!StartupManager.TrySetEnabled(
                     _settings.StartWithWindows,
                     out var startupError))
        {
            ShowTemporaryStatus(
                $"⚠ {startupError}",
                5000
            );
        }

        if (_settingsLoadError is null &&
            !_settings.OnboardingSeen)
            ShowFirstRunOnboarding();
    }

    private void InitializeSystemTray()
    {
        try
        {
            _trayService = new SystemTrayService(
                () => Dispatcher.BeginInvoke(ShowPalette),
                () => Dispatcher.BeginInvoke(
                    () => ExecuteCommand("settings")),
                () => Dispatcher.BeginInvoke(
                    () => ExecuteCommand("presets")),
                () => Dispatcher.BeginInvoke(
                    () => ExecuteCommand("help")),
                () => Dispatcher.BeginInvoke(
                    ExitApplication)
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not create tray icon: {ex}");
        }
    }

    private void ShowFirstRunOnboarding()
    {
        _settings.OnboardingSeen = true;

        var saveResult = SettingsManager.Save(_settings);

        if (!saveResult.Success)
        {
            Debug.WriteLine(
                $"Could not save onboarding state: {saveResult.Error}"
            );
        }

        ShowHelp(onboarding: true);
    }

    private void ExitApplication()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        _folderIndexCancellation?.Cancel();

        // Remove the icon before WPF starts closing windows so the shell
        // cannot keep a stale tray entry around.
        _trayService?.Dispose();
        _trayService = null;

        Application.Current.Shutdown();
    }

    private async Task RefreshQuickAsync()
    {
        if (_isQuickRefreshing)
        {
            ShowTemporaryStatus(
                "Refresh already running"
            );

            return;
        }

        _isQuickRefreshing = true;

        SetStatus(
            _isFolderIndexing
                ? "Refreshing apps and presets — folders still indexing..."
                : "Refreshing apps and presets..."
        );

        try
        {
            var presetResult =
                PresetManager.Reload();

            if (!presetResult.Success)
            {
                Debug.WriteLine(
                    $"Preset config error: {presetResult.Error}"
                );
            }

            // Expose preset changes without waiting for app discovery.
            RefreshResults();

            var apps =
                await AppIndexer.IndexAsync(
                    _settings.CustomApps
                );

            _apps = apps;

            RebuildItems();

            if (presetResult.Success)
            {
                if (_isFolderIndexing)
                {
                    ShowTemporaryStatus(
                        $"✓ Refreshed — " +
                        $"{_apps.Count} apps, " +
                        $"{presetResult.Count} presets — " +
                        $"folders still indexing..."
                    );
                }
                else
                {
                    ShowTemporaryStatus(
                        $"✓ Refreshed — " +
                        $"{_apps.Count} apps, " +
                        $"{presetResult.Count} presets"
                    );
                }
            }
            else
            {
                ShowTemporaryStatus(
                    "⚠ Apps refreshed, but presets.json has an error"
                );
            }

            Debug.WriteLine(
                $"Quick refresh complete: " +
                $"{_apps.Count} apps, " +
                $"{presetResult.Count} presets."
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);

            ShowTemporaryStatus(
                "⚠ Refresh failed"
            );
        }
        finally
        {
            _isQuickRefreshing = false;
        }
    }

    private void StartFolderIndex()
    {
        _folderIndexCancellation?.Cancel();
        _folderIndexCancellation?.Dispose();

        _folderIndexCancellation =
            new CancellationTokenSource();

        var generation = ++_folderIndexGeneration;

        _ = IndexFoldersAsync(
            generation,
            _folderIndexCancellation.Token
        );
    }

    private async Task IndexFoldersAsync(
        int generation,
        CancellationToken cancellationToken)
    {

        _isFolderIndexing = true;

        SetStatus(
            "Indexing folders..."
        );

        try
        {
            var folders =
                await FolderIndexer.IndexAsync(
                    _settings.IndexedFolders,
                    _settings.IgnoredFolders,
                    _settings.IgnoredFolderNames,
                    cancellationToken
                );

            if (generation != _folderIndexGeneration)
                return;

            _folders = folders;

            RebuildItems();

            ShowTemporaryStatus(
                $"✓ Indexed {_folders.Count} folders"
            );

            Debug.WriteLine(
                $"Folder index complete: " +
                $"{_folders.Count} folders."
            );
        }
        catch (OperationCanceledException)
        {
            // A settings change started a newer index.
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);

            ShowTemporaryStatus(
                "⚠ Folder indexing failed"
            );
        }
        finally
        {
            if (generation == _folderIndexGeneration)
            {
                _isFolderIndexing = false;
            }
        }
    }

    private void RebuildItems()
    {
        _items =
            _apps
                .Concat(_folders)
                .ToList();

        RefreshResults();
    }

    private void SetStatus(
        string text)
    {
        _statusRevision++;

        if (StatusText is not null)
        {
            StatusText.Text = text;
        }
    }

    private async void ShowTemporaryStatus(
        string text,
        int duration = 3000)
    {
        var revision =
            ++_statusRevision;

        if (StatusText is not null)
        {
            StatusText.Text = text;
        }

        await Task.Delay(
            duration
        );

        // Do not overwrite a newer status message.
        if (revision != _statusRevision)
            return;

        if (StatusText is not null)
        {
            StatusText.Text =
                _isFolderIndexing
                    ? "Indexing folders..."
                    : DefaultStatus;
        }
    }

    private void OnSourceInitialized(
        object? sender,
        EventArgs e)
    {
        var handle =
            new WindowInteropHelper(this).Handle;

        _source =
            HwndSource.FromHwnd(handle);

        _source.AddHook(
            WndProc
        );

        var registered = TryRegisterHotkey(
            handle,
            PrimaryHotkeyId,
            _settings.GlobalHotkey,
            out var error
        );

        if (registered)
        {
            _activeHotkeyId = PrimaryHotkeyId;
        }
        else
        {
            MessageBox.Show(
                $"Couldn't register {_settings.GlobalHotkey}.\n\n" +
                error,
                "Command Palette"
            );
        }
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg == WmSysCommand &&
            (wParam.ToInt64() & 0xFFF0) == ScKeyMenu)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WmHotkey &&
            _activeHotkeyId != 0 &&
            wParam.ToInt32() == _activeHotkeyId)
        {
            TogglePalette();

            handled = true;
        }

        return IntPtr.Zero;
    }

    private void TogglePalette()
    {
        if (IsVisible)
        {
            HidePalette();
        }
        else
        {
            ShowPalette();
        }
    }

    private void ShowPalette()
    {
        if (_isShuttingDown)
            return;

        Show();

        var handle = new WindowInteropHelper(this).Handle;

        ShowWindow(handle, 5);
        SetForegroundWindow(handle);
        Activate();

        SearchBox.Clear();

        RefreshResults();

        ResetResultsView();

        FocusSearchBox();

        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            FocusSearchBox
        );
    }

    private void FocusSearchBox()
    {
        if (!IsVisible)
            return;

        FocusManager.SetFocusedElement(this, SearchBox);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private void HidePalette()
    {
        CancelSystemCommandConfirmation();
        Hide();
    }

    private void OnDeactivated(
        object? sender,
        EventArgs e)
    {
        if (IsVisible &&
            _settings.HideWhenFocusIsLost)
        {
            HidePalette();
        }
    }

    private void SearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        CancelSystemCommandConfirmation();
        RefreshResults();
    }

    private void RefreshResults()
    {
        if (SearchBox is null ||
            ResultsList is null)
        {
            return;
        }

        var query =
            SearchBox.Text.Trim();

        var webResult =
            WebSearch.TryCreateResult(
                query,
                _settings.SearchProviders
            );

        if (webResult is not null)
        {
            SetResults(
                new List<PaletteItem>
                {
                    webResult
                },
                0
            );

            return;
        }

        var webHint =
            WebSearch.TryCreateHint(
                query,
                _settings.SearchProviders
            );

        if (webHint is not null)
        {
            SetResults(
                new List<PaletteItem>
                {
                    webHint
                },
                -1
            );

            return;
        }

        if (_settings.CalculatorEnabled)
        {
            var calculation =
                CalculatorService.TryCreateResult(query);

            if (calculation is not null)
            {
                SetResults([calculation], 0);
                return;
            }
        }

        var directUrl =
            DirectUrlDetector.TryCreateResult(query);

        if (directUrl is not null)
        {
            SetResults([directUrl], 0);
            return;
        }

        var searchableItems =
            PresetManager
                .GetPaletteItems()
                .Concat(BuiltInCommands)
                .Concat(_items)
                .Concat(
                    _settings.WindowsSettingsEnabled
                        ? WindowsSettingsCatalog.Items
                        : [])
                .Concat(
                    _settings.SystemCommandsEnabled
                        ? SystemCommandService.Items
                        : [])
                .ToList();

        List<PaletteItem> results;

        if (string.IsNullOrWhiteSpace(query))
        {
            results = CreateEmptyStateItems()
                .Take(_settings.MaximumResults)
                .ToList();
        }
        else
        {
            results =
                searchableItems

                    .Select(item => new
                    {
                        Item = item,

                        Score = new[]
                        {
                            FuzzyMatcher.Score(item.Name, query),
                            FuzzyMatcher.Score(item.Path, query),
                            FuzzyMatcher.Score(item.SearchText, query)
                        }.Max()
                    })

                    .Where(result =>
                        result.Score > 0
                    )

                    .OrderByDescending(result =>
                        result.Score
                    )

                    .ThenBy(result =>
                        result.Item.Name.Length
                    )

                    .Take(_settings.MaximumResults)

                    .Select(result =>
                        result.Item
                    )

                    .ToList();
        }

        SetResults(
            results,
            results.Count > 0 ? 0 : -1
        );
    }

    private IReadOnlyList<PaletteItem> CreateEmptyStateItems()
    {
        var items = BuiltInCommands
            .Where(item => item.Path is
                "settings" or "presets" or "help")
            .ToList();
        var provider = _settings.SearchProviders.FirstOrDefault(item =>
            item.Enabled);

        if (provider is not null)
        {
            items.Add(
                new PaletteItem(
                    $"Try: {provider.Prefix} command palette",
                    "",
                    PaletteItemType.Hint,
                    $"{provider.Name} web search example"
                )
            );
        }

        if (_settings.CalculatorEnabled)
        {
            items.Add(
                new PaletteItem(
                    "Try: 2x3",
                    "",
                    PaletteItemType.Hint,
                    "Calculator example"
                )
            );
        }

        items.Add(
            new PaletteItem(
                "Try: example.com",
                "",
                PaletteItemType.Hint,
                "Direct URL example"
            )
        );

        return items;
    }

    private void SetResults(
        IReadOnlyList<PaletteItem> results,
        int selectedIndex)
    {
        ResultsList.ItemsSource = results;
        ResultsList.SelectedIndex = selectedIndex;

        ResetResultsView();
    }

    private void ResetResultsView()
    {
        ResultsList.UpdateLayout();

        var scrollViewer =
            FindVisualChild<ScrollViewer>(ResultsList);

        scrollViewer?.ScrollToTop();
        scrollViewer?.ScrollToLeftEnd();
    }

    private static T? FindVisualChild<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0;
             i < VisualTreeHelper.GetChildrenCount(parent);
             i++)
        {
            var child =
                VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                return match;

            var descendant =
                FindVisualChild<T>(child);

            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    private async void SearchBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (IsBindingForAction(e, PaletteActionIds.Copy) &&
            SearchBox.SelectionLength > 0)
        {
            return;
        }

        if (TryGetBoundAction(e, out var actionId) &&
            ExecuteSelectedAction(actionId))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Down:

                MoveSelection(1);

                e.Handled = true;

                break;

            case Key.Up:

                MoveSelection(-1);

                e.Handled = true;

                break;

            case Key.Escape:

                HidePalette();

                e.Handled = true;

                break;

            case Key.F5:

                await RefreshQuickAsync();

                // Reuse an active folder scan instead of restarting it.
                if (!_isFolderIndexing)
                {
                    StartFolderIndex();
                }

                e.Handled = true;

                break;
        }
    }

    private bool TryGetBoundAction(
        KeyEventArgs e,
        out string actionId)
    {
        foreach (var candidate in _settings.ActionKeybindings.Keys)
        {
            if (IsBindingForAction(e, candidate))
            {
                actionId = candidate;
                return true;
            }
        }

        actionId = "";
        return false;
    }

    private bool IsBindingForAction(
        KeyEventArgs e,
        string actionId)
    {
        if (!_settings.ActionKeybindings.TryGetValue(
                actionId,
                out var binding) ||
            !SettingsManager.TryParseKeybinding(
                binding,
                out var gesture,
                out _))
        {
            return false;
        }

        var key = e.Key == Key.System
            ? e.SystemKey
            : e.Key;

        return key == gesture.Key &&
               Keyboard.Modifiers == gesture.Modifiers;
    }

    private bool ExecuteSelectedAction(
        string actionId)
    {
        if (ResultsList.SelectedItem is not PaletteItem item ||
            !PaletteActionCatalog.Supports(item, actionId))
        {
            return false;
        }

        if (actionId.Equals(
                PaletteActionIds.Copy,
                StringComparison.OrdinalIgnoreCase))
        {
            return CopyItemValue(item, hideAfterCopy: false);
        }

        if (actionId.Equals(
                PaletteActionIds.OpenTerminalHere,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!TerminalLauncher.TryOpen(item.Path, out var error))
            {
                ShowTemporaryStatus($"⚠ {error}", 5000);
                return true;
            }

            HidePalette();
            return true;
        }

        if (actionId.Equals(
                PaletteActionIds.Open,
                StringComparison.OrdinalIgnoreCase))
        {
            OpenSelectedItem();
            return true;
        }

        return false;
    }

    private bool CopyItemValue(
        PaletteItem item,
        bool hideAfterCopy)
    {
        var value = PaletteActionCatalog.GetCopyText(item);

        if (value is null)
            return false;

        try
        {
            Clipboard.SetText(value);

            if (hideAfterCopy)
                HidePalette();
            else
                ShowTemporaryStatus("✓ Copied");
        }
        catch (Exception ex)
        {
            ShowTemporaryStatus($"⚠ Copy failed: {ex.Message}", 5000);
        }

        return true;
    }

    private void MoveSelection(
        int direction)
    {
        if (ResultsList.Items.Count == 0)
            return;

        var next =
            ResultsList.SelectedIndex +
            direction;

        if (next < 0)
        {
            next =
                ResultsList.Items.Count - 1;
        }

        if (next >= ResultsList.Items.Count)
        {
            next = 0;
        }

        ResultsList.SelectedIndex =
            next;

        ResultsList.ScrollIntoView(
            ResultsList.SelectedItem
        );

        FindVisualChild<ScrollViewer>(
            ResultsList
        )?.ScrollToLeftEnd();
    }

    private void OpenSelectedItem()
    {
        if (ResultsList.SelectedItem
            is not PaletteItem item)
        {
            return;
        }

        if (item.Type ==
            PaletteItemType.Hint)
        {
            return;
        }

        if (item.Type == PaletteItemType.Calculator)
        {
            CopyItemValue(item, hideAfterCopy: true);
            return;
        }

        if (item.Type == PaletteItemType.SystemCommand)
        {
            ExecuteSystemCommand(item);
            return;
        }

        if (item.Type ==
            PaletteItemType.Command)
        {
            ExecuteCommand(
                item.Path
            );

            return;
        }

        if (item.Type ==
            PaletteItemType.Preset)
        {
            ExecutePreset(
                item.Path
            );

            HidePalette();

            return;
        }

        try
        {
            OpenTarget(
                item.Path
            );

            HidePalette();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Não consegui abrir:\n" +
                $"{item.Path}\n\n" +
                $"{exception.Message}"
            );
        }
    }

    private void ExecuteSystemCommand(
        PaletteItem item)
    {
        if (_settings.ConfirmDestructiveSystemActions &&
            SystemCommandService.IsDestructive(item.Path))
        {
            if (!string.Equals(
                    _armedSystemCommandId,
                    item.Path,
                    StringComparison.OrdinalIgnoreCase) ||
                DateTime.UtcNow > _confirmationExpiresAt)
            {
                ArmSystemCommandConfirmation(item);
                return;
            }
        }

        CancelSystemCommandConfirmation();

        if (!SystemCommandService.TryExecute(
                item.Path,
                out var error))
        {
            ShowTemporaryStatus($"⚠ {error}", 5000);
            return;
        }

        HidePalette();
    }

    private void ArmSystemCommandConfirmation(
        PaletteItem item)
    {
        _armedSystemCommandId = item.Path;
        _confirmationExpiresAt = DateTime.UtcNow.AddSeconds(8);
        UpdateAvailableActions();
        _ = ExpireSystemCommandConfirmationAsync(item.Path);
    }

    private async Task ExpireSystemCommandConfirmationAsync(
        string commandId)
    {
        await Task.Delay(8000);

        if (string.Equals(
                _armedSystemCommandId,
                commandId,
                StringComparison.OrdinalIgnoreCase) &&
            DateTime.UtcNow >= _confirmationExpiresAt)
        {
            CancelSystemCommandConfirmation();
        }
    }

    private void CancelSystemCommandConfirmation()
    {
        if (_armedSystemCommandId is null)
            return;

        _armedSystemCommandId = null;
        UpdateAvailableActions();
    }

    private void ResultsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        CancelSystemCommandConfirmation();
        UpdateAvailableActions();
    }

    private void UpdateAvailableActions()
    {
        if (ActionsText is null ||
            ResultsList?.SelectedItem is not PaletteItem item)
        {
            if (ActionsText is not null)
                ActionsText.Text = "";

            return;
        }

        if (_armedSystemCommandId is not null &&
            _armedSystemCommandId.Equals(
                item.Path,
                StringComparison.OrdinalIgnoreCase))
        {
            var openBinding =
                _settings.ActionKeybindings[PaletteActionIds.Open];

            ActionsText.Text =
                $"{openBinding} again to confirm {item.Name}";

            return;
        }

        ActionsText.Text = string.Join(
            "    ",
            PaletteActionCatalog.GetActions(item)
                .Select(action =>
                    $"{_settings.ActionKeybindings[action.Id]} {action.Label}")
        );
    }

    private void ExecuteCommand(
        string command)
    {
        switch (command)
        {
            case "presets":
                ShowPresetEditor();
                break;

            case "settings":
                ShowSettings();
                break;

            case "help":
                ShowHelp(onboarding: false);
                break;

            case "exit":
                ExitApplication();
                break;
        }
    }

    private void ShowPresetEditor()
    {
        HidePalette();

        if (_presetEditorWindow is not null)
        {
            BringWindowToFront(_presetEditorWindow);
            return;
        }

        var editor = new PresetEditorWindow(_apps);
        _presetEditorWindow = editor;
        editor.Closed += (_, _) =>
        {
            _presetEditorWindow = null;
            ReloadPresetsAfterEditor();
        };
        editor.Show();
        BringWindowToFront(editor);
    }

    private void ReloadPresetsAfterEditor()
    {
        var result = PresetManager.Reload();

        if (!result.Success)
        {
            Debug.WriteLine($"Preset reload failed: {result.Error}");
            ShowTemporaryStatus(
                $"⚠ presets.json: {result.Error}",
                5000
            );
            return;
        }

        RefreshResults();
    }

    private void ShowSettings()
    {
        HidePalette();

        if (_settingsWindow is not null)
        {
            BringWindowToFront(_settingsWindow);
            return;
        }

        var settingsWindow = new SettingsWindow(
            SettingsManager.Clone(_settings),
            ApplySettings,
            SuspendHotkeyForRecording,
            ResumeHotkeyAfterRecording
        );
        _settingsWindow = settingsWindow;
        settingsWindow.Closed += (_, _) =>
            _settingsWindow = null;
        settingsWindow.Show();
        BringWindowToFront(settingsWindow);
    }

    private void ShowHelp(bool onboarding)
    {
        HidePalette();

        if (_helpWindow is not null)
        {
            BringWindowToFront(_helpWindow);
            return;
        }

        var helpWindow = new HelpWindow(
            SettingsManager.Clone(_settings),
            onboarding
        );
        _helpWindow = helpWindow;
        helpWindow.Closed += (_, _) =>
            _helpWindow = null;
        helpWindow.Show();
        BringWindowToFront(helpWindow);
    }

    private void ExecutePreset(
        string presetId)
    {
        var preset =
            PresetManager.GetById(
                presetId
            );

        if (preset is null)
            return;

        Debug.WriteLine(
            $"Executing preset: {preset.Name}"
        );

        foreach (var action
                 in preset.Actions)
        {
            switch (action.Type)
            {
                case PresetActionType.OpenApp:

                    OpenAppByName(
                        action.Target
                    );

                    break;

                case PresetActionType.OpenUrl:

                    OpenTarget(
                        action.Target
                    );

                    break;

                case PresetActionType.OpenPath:

                    OpenTarget(
                        action.Target
                    );

                    break;

                case PresetActionType.CloseProcess:

                    CloseProcess(
                        action.Target
                    );

                    break;

                case PresetActionType.CloseApp:

                    CloseAppByName(
                        action.Target,
                        closeAllWindows: false
                    );

                    break;

                case PresetActionType.CloseAppWindows:

                    CloseAppByName(
                        action.Target,
                        closeAllWindows: true
                    );

                    break;
            }
        }
    }

    private void OpenAppByName(
        string appName)
    {
        var app =
            FindAppByName(appName);

        if (app is null)
        {
            Debug.WriteLine(
                $"Preset app not found: {appName}"
            );

            return;
        }

        OpenTarget(
            app.Path
        );
    }

    private void CloseAppByName(
        string appName,
        bool closeAllWindows)
    {
        var app = FindAppByName(appName);

        if (app is null)
        {
            Debug.WriteLine(
                $"Preset app not found: {appName}"
            );

            return;
        }

        if (closeAllWindows)
        {
            ApplicationCloser.CloseApplicationWindows(app);
        }
        else
        {
            ApplicationCloser.CloseApplication(app);
        }
    }

    private static void BringWindowToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void OnPreviewKeyUp(
        object sender,
        KeyEventArgs e)
    {
        var key = e.Key == Key.System
            ? e.SystemKey
            : e.Key;

        if (IsVisible &&
            key is Key.LeftAlt or Key.RightAlt)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                FocusSearchBox
            );
        }
    }

    private PaletteItem? FindAppByName(
        string appName)
    {
        return _apps
            .OrderByDescending(item =>
                item.Name.Equals(
                    appName,
                    StringComparison.OrdinalIgnoreCase
                ))
            .ThenByDescending(item =>
                item.Name.StartsWith(
                    appName,
                    StringComparison.OrdinalIgnoreCase
                ))
            .FirstOrDefault(item =>
                item.Name.Contains(
                    appName,
                    StringComparison.OrdinalIgnoreCase
                ));
    }

    private string? ApplySettings(
        AppSettings settings)
    {
        settings.OnboardingSeen =
            _settings.OnboardingSeen || settings.OnboardingSeen;
        settings = SettingsManager.NormalizeCopy(settings);

        if (!SettingsManager.TryValidate(settings, out var error))
            return error;

        var handle = new WindowInteropHelper(this).Handle;
        var hotkeyChanged =
            _activeHotkeyId == 0 ||
            !_settings.GlobalHotkey.Equals(
                settings.GlobalHotkey,
                StringComparison.OrdinalIgnoreCase
            );

        var replacementHotkeyId =
            _activeHotkeyId == PrimaryHotkeyId
                ? SecondaryHotkeyId
                : PrimaryHotkeyId;

        if (hotkeyChanged &&
            !TryRegisterHotkey(
                handle,
                replacementHotkeyId,
                settings.GlobalHotkey,
                out error))
        {
            return error;
        }

        var startupChanged =
            _settings.StartWithWindows != settings.StartWithWindows;

        if (startupChanged &&
            !StartupManager.TrySetEnabled(
                settings.StartWithWindows,
                out error))
        {
            if (hotkeyChanged)
            {
                UnregisterHotKey(handle, replacementHotkeyId);
            }

            return error;
        }

        var saveResult = SettingsManager.Save(settings);

        if (!saveResult.Success)
        {
            if (hotkeyChanged)
            {
                UnregisterHotKey(handle, replacementHotkeyId);
            }

            if (startupChanged)
            {
                StartupManager.TrySetEnabled(
                    _settings.StartWithWindows,
                    out _
                );
            }

            return saveResult.Error;
        }

        if (hotkeyChanged)
        {
            if (_activeHotkeyId != 0)
            {
                UnregisterHotKey(handle, _activeHotkeyId);
            }

            _activeHotkeyId = replacementHotkeyId;
        }

        var folderSettingsChanged =
            !HaveSameValues(
                _settings.IndexedFolders,
                settings.IndexedFolders) ||
            !HaveSameValues(
                _settings.IgnoredFolders,
                settings.IgnoredFolders) ||
            !HaveSameValues(
                _settings.IgnoredFolderNames,
                settings.IgnoredFolderNames);

        var customAppsChanged =
            !HaveSameCustomApps(
                _settings.CustomApps,
                settings.CustomApps
            );

        _settings = SettingsManager.Clone(settings);

        RefreshResults();

        if (folderSettingsChanged)
        {
            StartFolderIndex();
        }

        if (customAppsChanged)
        {
            _ = RefreshQuickAsync();
        }

        return null;
    }

    private static bool HaveSameValues(
        IEnumerable<string> first,
        IEnumerable<string> second)
    {
        return first.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(second);
    }

    private static bool HaveSameCustomApps(
        IEnumerable<CustomApplication> first,
        IEnumerable<CustomApplication> second)
    {
        var firstValues = first.Select(app =>
            $"{app.Name.Trim()}\0{app.Path.Trim()}");

        var secondValues = second.Select(app =>
            $"{app.Name.Trim()}\0{app.Path.Trim()}");

        return firstValues
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(secondValues);
    }

    private string? SuspendHotkeyForRecording()
    {
        if (_isHotkeySuspended)
            return null;

        if (_activeHotkeyId == 0)
        {
            return null;
        }

        var handle = new WindowInteropHelper(this).Handle;

        if (!UnregisterHotKey(handle, _activeHotkeyId))
        {
            return "Couldn't temporarily release the current hotkey.";
        }

        _isHotkeySuspended = true;
        return null;
    }

    private void ResumeHotkeyAfterRecording()
    {
        if (_isShuttingDown)
        {
            _isHotkeySuspended = false;
            return;
        }

        if (!_isHotkeySuspended)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        _isHotkeySuspended = false;

        if (!TryRegisterHotkey(
                handle,
                _activeHotkeyId,
                _settings.GlobalHotkey,
                out var error))
        {
            _activeHotkeyId = 0;

            MessageBox.Show(
                error,
                "Command Palette"
            );
        }
    }

    private static bool TryRegisterHotkey(
        IntPtr handle,
        int hotkeyId,
        string hotkeyText,
        out string? error)
    {
        if (!SettingsManager.TryParseHotkey(
                hotkeyText,
                out var hotkey,
                out error))
        {
            return false;
        }

        var modifiers = ModNoRepeat;

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt))
            modifiers |= ModAlt;

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control))
            modifiers |= ModControl;

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift))
            modifiers |= ModShift;

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows))
            modifiers |= ModWin;

        var registered = RegisterHotKey(
            handle,
            hotkeyId,
            modifiers,
            (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)
        );

        error = registered
            ? null
            : $"The hotkey {hotkeyText} is already in use or unavailable.";

        return registered;
    }

    private static void OpenTarget(
        string target)
    {
        try
        {
            if (target.StartsWith(
                    AppIndexer.ShellAppPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                var appUserModelId =
                    target[AppIndexer.ShellAppPrefix.Length..];

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments =
                            $"shell:AppsFolder\\{appUserModelId}",
                        UseShellExecute = true
                    }
                );

                return;
            }

            if (target.StartsWith(
                    AppIndexer.ShellPathPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                target =
                    target[AppIndexer.ShellPathPrefix.Length..];
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                }
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Failed to open {target}: {ex}"
            );
        }
    }

    private static void CloseProcess(
        string processName)
    {
        try
        {
            ApplicationCloser.CloseProcessesByName(processName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Failed to close {processName}: {ex}"
            );
        }
    }

    private void OnClosed(
        object? sender,
        EventArgs e)
    {
        _isShuttingDown = true;

        var handle =
            new WindowInteropHelper(this).Handle;

        _folderIndexCancellation?.Cancel();
        _folderIndexCancellation?.Dispose();
        _trayService?.Dispose();
        _trayService = null;

        if (_activeHotkeyId != 0)
        {
            UnregisterHotKey(
                handle,
                _activeHotkeyId
            );

            _activeHotkeyId = 0;
        }

        _isHotkeySuspended = false;

        _source?.RemoveHook(
            WndProc
        );
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk
    );

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(
        IntPtr hWnd,
        int id
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(
        IntPtr windowHandle
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(
        IntPtr windowHandle,
        int command
    );
}

public enum PaletteItemType
{
    Folder,
    App,
    WebSearch,
    Url,
    Calculator,
    WindowsSetting,
    SystemCommand,
    Hint,
    Preset,
    Command
}

public class PaletteItem
{
    public string Name { get; }

    public string Path { get; }

    public string Subtitle { get; }

    public string SearchText { get; }

    public PaletteItemType Type { get; }

    public PaletteItem(
        string name,
        string path,
        PaletteItemType type,
        string? subtitle = null,
        string? searchText = null)
    {
        Name = name;

        Path = path;

        Type = type;

        Subtitle =
            subtitle ?? path;

        SearchText = searchText ?? "";
    }
}
