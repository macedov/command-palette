using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CommandPalette;

public partial class MainWindow : Window
{
    private const int HotkeyId = 9000;

    private const uint ModAlt = 0x0001;
    private const uint VkSpace = 0x20;

    private const int WmHotkey = 0x0312;

    private HwndSource? _source;

    private static readonly List<PaletteItem> BuiltInCommands =
    [
        new PaletteItem(
            "Presets",
            "presets",
            PaletteItemType.Command,
            "Manage presets"
        )
    ];

    // Mantemos apps e folders separados.
    // Assim um refresh de apps não destrói os folders
    // que já foram indexados.
    private List<PaletteItem> _apps = [];
    private List<PaletteItem> _folders = [];

    // Lista combinada usada pela busca.
    private List<PaletteItem> _items = [];

    private bool _isQuickRefreshing;
    private bool _isFolderIndexing;

    private const string DefaultStatus =
        "↑ ↓ Navigate     Enter Open     F5 Refresh     Esc Close";

    private int _statusRevision;

    public MainWindow()
    {
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        Deactivated += OnDeactivated;
    }

    // ============================================================
    // STARTUP
    // ============================================================

    private async void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Hide();

        // Primeiro carrega o que é rápido:
        // presets + apps.
        await RefreshQuickAsync();

        // Depois começa a indexação pesada das pastas
        // sem esperar ela terminar.
        _ = IndexFoldersAsync();
    }

    // ============================================================
    // QUICK REFRESH
    // Presets + Apps
    // ============================================================

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
            // --------------------------------------------------------
            // PRESETS
            // --------------------------------------------------------

            var presetResult =
                PresetManager.Reload();

            if (!presetResult.Success)
            {
                Debug.WriteLine(
                    $"Preset config error: {presetResult.Error}"
                );
            }

            // Atualiza imediatamente os presets visíveis.
            // Não precisamos esperar AppIndexer terminar.
            RefreshResults();

            // --------------------------------------------------------
            // APPS
            // --------------------------------------------------------

            var apps =
                await AppIndexer.IndexAsync();

            _apps = apps;

            RebuildItems();

            // --------------------------------------------------------
            // STATUS
            // --------------------------------------------------------

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

    // ============================================================
    // FOLDER INDEX
    // ============================================================

    private async Task IndexFoldersAsync()
    {
        // Muito importante:
        // nunca iniciamos dois indexes de folder ao mesmo tempo.
        if (_isFolderIndexing)
            return;

        _isFolderIndexing = true;

        SetStatus(
            "Indexing folders..."
        );

        try
        {
            var userFolder =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                );

            var roots =
                new List<string>
                {
                    @"D:\",
                    userFolder
                };

            var folders =
                await FolderIndexer.IndexAsync(
                    roots
                );

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
        catch (Exception ex)
        {
            Debug.WriteLine(ex);

            ShowTemporaryStatus(
                "⚠ Folder indexing failed"
            );
        }
        finally
        {
            _isFolderIndexing = false;
        }
    }

    // ============================================================
    // ITEMS
    // ============================================================

    private void RebuildItems()
    {
        _items =
            _apps
                .Concat(_folders)
                .ToList();

        RefreshResults();
    }

    // ============================================================
    // STATUS
    // ============================================================

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

        // Se outra mensagem apareceu depois,
        // não sobrescrevemos ela.
        if (revision != _statusRevision)
            return;

        if (StatusText is not null)
        {
            // Se ainda estiver indexando,
            // volta para esse status em vez
            // do texto padrão.
            StatusText.Text =
                _isFolderIndexing
                    ? "Indexing folders..."
                    : DefaultStatus;
        }
    }

    // ============================================================
    // GLOBAL HOTKEY
    // ============================================================

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

        var registered =
            RegisterHotKey(
                handle,
                HotkeyId,
                ModAlt,
                VkSpace
            );

        if (!registered)
        {
            MessageBox.Show(
                "Não consegui registrar Alt + Space.\n\n" +
                "Provavelmente outro programa já está usando essa hotkey.",
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
        if (msg == WmHotkey &&
            wParam.ToInt32() == HotkeyId)
        {
            TogglePalette();

            handled = true;
        }

        return IntPtr.Zero;
    }

    // ============================================================
    // SHOW / HIDE
    // ============================================================

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
        Show();

        Activate();

        SearchBox.Clear();

        RefreshResults();

        SearchBox.Focus();

        Keyboard.Focus(
            SearchBox
        );
    }

    private void HidePalette()
    {
        Hide();
    }

    private void OnDeactivated(
        object? sender,
        EventArgs e)
    {
        if (IsVisible)
            HidePalette();
    }

    // ============================================================
    // SEARCH
    // ============================================================

    private void SearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
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

        // --------------------------------------------------------
        // WEB SEARCH
        // --------------------------------------------------------

        var webResult =
            WebSearch.TryCreateResult(
                query
            );

        if (webResult is not null)
        {
            ResultsList.ItemsSource =
                new List<PaletteItem>
                {
                    webResult
                };

            ResultsList.SelectedIndex = 0;

            return;
        }

        // --------------------------------------------------------
        // WEB HINT
        // --------------------------------------------------------

        var webHint =
            WebSearch.TryCreateHint(
                query
            );

        if (webHint is not null)
        {
            ResultsList.ItemsSource =
                new List<PaletteItem>
                {
                    webHint
                };

            ResultsList.SelectedIndex = -1;

            return;
        }

        // --------------------------------------------------------
        // NORMAL RESULTS
        // Apps + folders + presets
        // --------------------------------------------------------

        var searchableItems =
            PresetManager
                .GetPaletteItems()
                .Concat(BuiltInCommands)
                .Concat(_items)
                .ToList();

        List<PaletteItem> results;

        if (string.IsNullOrWhiteSpace(query))
        {
            results =
                searchableItems
                    .Take(20)
                    .ToList();
        }
        else
        {
            results =
                searchableItems

                    .Select(item => new
                    {
                        Item = item,

                        Score = Math.Max(
                            FuzzyMatcher.Score(
                                item.Name,
                                query
                            ),

                            FuzzyMatcher.Score(
                                item.Path,
                                query
                            )
                        )
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

                    .Take(30)

                    .Select(result =>
                        result.Item
                    )

                    .ToList();
        }

        ResultsList.ItemsSource =
            results;

        if (results.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
        }
    }

    // ============================================================
    // KEYBOARD
    // ============================================================

    private async void SearchBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
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

            case Key.Enter:

                OpenSelectedItem();

                e.Handled = true;

                break;

            case Key.Escape:

                HidePalette();

                e.Handled = true;

                break;

            case Key.F5:

                // Presets e apps são atualizados imediatamente,
                // mesmo se folders ainda estiverem sendo indexados.
                await RefreshQuickAsync();

                // Se o folder index não estiver rodando,
                // inicia um novo.
                //
                // Se já estiver rodando, deixa ele continuar
                // normalmente em vez de reiniciar.
                if (!_isFolderIndexing)
                {
                    _ = IndexFoldersAsync();
                }

                e.Handled = true;

                break;
        }
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
    }

    // ============================================================
    // OPEN ITEM
    // ============================================================

    private void OpenSelectedItem()
    {
        if (ResultsList.SelectedItem
            is not PaletteItem item)
        {
            return;
        }

        // Hint não executa nada.
        if (item.Type ==
            PaletteItemType.Hint)
        {
            return;
        }

        // Comandos internos da palette.
        if (item.Type ==
            PaletteItemType.Command)
        {
            ExecuteCommand(
                item.Path
            );

            return;
        }

        // Preset possui execução própria.
        if (item.Type ==
            PaletteItemType.Preset)
        {
            ExecutePreset(
                item.Path
            );

            HidePalette();

            return;
        }

        // App / Folder / Web Search
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

    // ============================================================
    // BUILT-IN COMMANDS
    // ============================================================

    private void ExecuteCommand(
        string command)
    {
        switch (command)
        {
            case "presets":

                HidePalette();

                var editor =
                    new PresetEditorWindow();

                editor.ShowDialog();

                // Garante que qualquer alteração feita
                // no editor esteja imediatamente disponível
                // quando voltarmos para a palette.
                var result =
                    PresetManager.Reload();

                if (!result.Success)
                {
                    Debug.WriteLine(
                        $"Preset reload failed: {result.Error}"
                    );

                    ShowTemporaryStatus(
                        $"⚠ presets.json: {result.Error}",
                        5000
                    );
                }
                else
                {
                    RefreshResults();

                    ShowTemporaryStatus(
                        $"✓ Loaded {result.Count} presets"
                    );
                }

                break;
        }
    }

    // ============================================================
    // PRESETS
    // ============================================================

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
            }
        }
    }

    // ============================================================
    // ACTIONS
    // ============================================================

    private void OpenAppByName(
        string appName)
    {
        // Agora podemos procurar diretamente
        // na lista de apps.
        var app =
            _apps

                .OrderByDescending(item =>
                    item.Name.Equals(
                        appName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )

                .ThenByDescending(item =>
                    item.Name.StartsWith(
                        appName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )

                .FirstOrDefault(item =>
                    item.Name.Contains(
                        appName,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

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

    private static void OpenTarget(
        string target)
    {
        try
        {
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
            var processes =
                Process.GetProcessesByName(
                    processName
                );

            foreach (var process
                     in processes)
            {
                try
                {
                    process.CloseMainWindow();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Failed to close {processName}: {ex}"
            );
        }
    }

    // ============================================================
    // CLOSE
    // ============================================================

    private void OnClosed(
        object? sender,
        EventArgs e)
    {
        var handle =
            new WindowInteropHelper(this).Handle;

        UnregisterHotKey(
            handle,
            HotkeyId
        );

        _source?.RemoveHook(
            WndProc
        );
    }

    // ============================================================
    // WIN32
    // ============================================================

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
}

// ================================================================
// PALETTE ITEM
// ================================================================

public enum PaletteItemType
{
    Folder,
    App,
    WebSearch,
    Hint,
    Preset,
    Command
}

public class PaletteItem
{
    public string Name { get; }

    public string Path { get; }

    public string Subtitle { get; }

    public PaletteItemType Type { get; }

    public PaletteItem(
        string name,
        string path,
        PaletteItemType type,
        string? subtitle = null)
    {
        Name = name;

        Path = path;

        Type = type;

        Subtitle =
            subtitle ?? path;
    }
}
