using System;
using Microsoft.Win32;

namespace CommandPalette;

internal static class StartupManager
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "CommandPalette";

    public static bool TrySetEnabled(
        bool enabled,
        out string? error)
    {
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                writable: true
            );

            if (runKey is null)
            {
                error = "Couldn't open the Windows startup settings.";
                return false;
            }

            if (enabled)
            {
                var executablePath = Environment.ProcessPath;

                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    error = "Couldn't determine the application path.";
                    return false;
                }

                runKey.SetValue(
                    ValueName,
                    $"\"{executablePath}\"",
                    RegistryValueKind.String
                );
            }
            else
            {
                runKey.DeleteValue(
                    ValueName,
                    throwOnMissingValue: false
                );
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Couldn't update Windows startup: {ex.Message}";
            return false;
        }
    }
}
