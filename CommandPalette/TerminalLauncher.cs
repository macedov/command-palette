using System;
using System.Diagnostics;

namespace CommandPalette;

internal static class TerminalLauncher
{
    public static bool TryOpen(
        string directory,
        out string? error)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wt.exe",
                UseShellExecute = true
            };

            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(directory);

            Process.Start(startInfo);
            error = null;
            return true;
        }
        catch
        {
            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        WorkingDirectory = directory,
                        UseShellExecute = true
                    }
                );

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
