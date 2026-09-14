# Command Palette

A lightweight and customizable command palette for Windows.

Quickly launch applications, open folders, search the web, run commands, use presets, and more — all from a keyboard-first interface.

> Current version: **v0.1.0**

## Features

- Fast application search
- Folder search with fuzzy matching
- Customizable global hotkey
- Configurable web search providers
- Direct URL opening
- Built-in calculator
- Custom presets
- Windows Settings shortcuts
- System commands
- Configurable keybindings
- System tray integration
- Start with Windows
- Settings, Help, and first-run onboarding
- Keyboard-first navigation

## Usage

By default, press:

```text
Alt + Space
```

Then start typing.

Examples:

```text
discord
```

Launch an installed application.

```text
downloads
```

Find and open a folder.

```text
2x512
```

Calculate an expression.

```text
github.com
```

Open a URL directly.

```text
g wpf tutorial
```

Search using a configured web search provider.

You can change the global hotkey, search providers, keybindings, indexed folders, and other behavior from **Settings**.

## Presets

Presets let you create reusable actions that can be launched directly from the palette.

They can be used to:

- Open applications
- Open folders and paths
- Open URLs
- Close applications or windows
- Perform other supported actions

Open the preset editor by searching for:

```text
presets
```

## Built-in Commands

Some useful built-in commands include:

```text
settings
presets
help
exit
quit
```

Additional Windows and system commands can also be enabled from Settings.

## Installation

1. Go to the **Releases** page.
2. Download:

```text
CommandPalette-Setup-0.1.0.exe
```

3. Run the installer.
4. Open **Command Palette** from the Start Menu.
5. Press `Alt + Space`.

The installer is self-contained, so you **do not need to install .NET separately**.

### Requirements

- Windows
- x64

## Updating

Newer installers can be installed over an existing installation.

Your settings and presets are stored separately in:

```text
%AppData%\CommandPalette
```

and are preserved between updates.

## Uninstalling

Command Palette can be removed normally through **Installed apps** in Windows Settings.

Your configuration in `%AppData%\CommandPalette` is preserved after uninstalling.

## SmartScreen Notice

Command Palette is currently **not digitally signed**.

Because of this, Windows SmartScreen may display an **Unknown publisher** warning when running the installer.

If you downloaded the installer from this repository's official Releases page, this is expected for the current version.

## Building from Source

### Requirements

- Windows
- .NET 10 SDK

Clone the repository:

```bash
git clone https://github.com/macedov/command-palette.git
cd command-palette
```

Build:

```bash
dotnet build
```

To create the Windows x64 self-contained publish:

```bash
dotnet publish ".\CommandPalette\CommandPalette.csproj" -c Release -r win-x64 --self-contained true
```

## Configuration

Application configuration is stored in:

```text
%AppData%\CommandPalette\settings.json
```

Presets are stored in:

```text
%AppData%\CommandPalette\presets.json
```

Most options can be configured directly through the application's UI.

## Status

Command Palette is currently in its early stages.

The focus is keeping it:

- fast
- lightweight
- clean
- keyboard-first
- customizable without unnecessary complexity

Feedback and bug reports are welcome.
