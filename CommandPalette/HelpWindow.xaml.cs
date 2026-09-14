using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CommandPalette;

public partial class HelpWindow : Window
{
    public HelpWindow(
        AppSettings settings,
        bool onboarding = false)
    {
        InitializeComponent();
        ApplicationIconProvider.ApplyTo(this);
        WindowSystemMenu.Suppress(this);

        WindowTitleText.Text = onboarding
            ? "Welcome to Command Palette"
            : "Command Palette Help";

        if (onboarding)
            BuildOnboarding(settings);
        else
            BuildHelp(settings);
    }

    private void BuildOnboarding(AppSettings settings)
    {
        AddSection(
            "Start typing",
            $"Press {settings.GlobalHotkey} to open the palette, then type an application or folder name and press Enter."
        );

        var examples = new List<string>();
        var provider = settings.SearchProviders.FirstOrDefault(item =>
            item.Enabled);

        if (provider is not null)
            examples.Add($"{provider.Prefix} command palette");

        if (settings.CalculatorEnabled)
            examples.Add("2x3");

        examples.Add("github.com/OpenAI");

        AddSection("Try these", string.Join("    ", examples));
        AddSection(
            "Explore",
            "Type settings, presets, or help at any time. The footer shows the shortcuts available for the selected result."
        );
        AddNote("This welcome is shown once. The full guide remains available through Help.");
    }

    private void BuildHelp(AppSettings settings)
    {
        AddSection(
            "Apps and folders",
            $"Press {settings.GlobalHotkey}, type part of an application or folder name, use ↑/↓ to select, and run the highlighted result."
        );

        var providers = settings.SearchProviders
            .Where(item => item.Enabled)
            .Select(item => $"{item.Prefix} query — {item.Name}")
            .ToList();

        if (providers.Count > 0)
            AddSection("Web search", string.Join("\n", providers));

        if (settings.CalculatorEnabled)
        {
            AddSection(
                "Calculator",
                "Type an expression such as 2x3, (20+5)*4, or 2^10. Running the result copies it."
            );
        }

        if (settings.WindowsSettingsEnabled)
        {
            AddSection(
                "Windows Settings",
                string.Join("  •  ", WindowsSettingsCatalog.Items.Select(item => item.Name))
            );
        }

        if (settings.SystemCommandsEnabled)
        {
            AddSection(
                "System commands",
                string.Join("  •  ", SystemCommandService.Items.Select(item => item.Name))
            );
        }

        AddSection(
            "Settings and presets",
            "Search for Settings to customize the palette, or Presets to create multi-action commands."
        );

        AddSection(
            "Result actions",
            string.Join(
                "\n",
                $"{settings.ActionKeybindings[PaletteActionIds.Open]} — Open / run",
                $"{settings.ActionKeybindings[PaletteActionIds.Copy]} — Copy when available",
                $"{settings.ActionKeybindings[PaletteActionIds.OpenTerminalHere]} — Open terminal in a selected folder"
            )
        );
    }

    private void AddSection(string title, string body)
    {
        SectionsPanel.Children.Add(
            new TextBlock
            {
                Text = title,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            }
        );

        SectionsPanel.Children.Add(
            new TextBlock
            {
                Text = body,
                FontSize = 13,
                Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(176, 176, 176)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 0, 0, 20)
            }
        );
    }

    private void AddNote(string text)
    {
        SectionsPanel.Children.Add(
            new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(128, 128, 128)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            }
        );
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
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
