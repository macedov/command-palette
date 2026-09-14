using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CommandPalette;

internal static class ApplicationCloser
{
    public static void CloseApplication(
        PaletteItem app)
    {
        var processes = FindProcesses(app);

        if (IsExplorer(app, processes))
        {
            ExplorerWindowManager.CloseOpenWindows();
            DisposeAll(processes);
            return;
        }

        TerminateAll(processes, app.Name);
    }

    public static void CloseProcessesByName(
        string processName)
    {
        var normalizedName = Path.GetFileNameWithoutExtension(
            processName.Trim().Trim('"')
        );

        if (normalizedName.Equals(
                "explorer",
                StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Equals(
                "file explorer",
                StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Equals(
                "windows explorer",
                StringComparison.OrdinalIgnoreCase))
        {
            ExplorerWindowManager.CloseOpenWindows();
            return;
        }

        TerminateAll(
            Process.GetProcessesByName(normalizedName),
            processName
        );
    }

    private static void TerminateAll(
        IEnumerable<Process> processes,
        string displayName)
    {
        foreach (var process in processes)
        {
            try
            {
                // Tray applications commonly interpret WM_CLOSE as
                // "hide to tray". These actions promise a complete exit.
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Failed to close {displayName}: {ex}"
                );
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public static void CloseApplicationWindows(
        PaletteItem app)
    {
        var processes = FindProcesses(app);

        try
        {
            if (processes.Any(process =>
                    process.ProcessName.Equals(
                        "explorer",
                        StringComparison.OrdinalIgnoreCase)))
            {
                ExplorerWindowManager.CloseOpenWindows();
                return;
            }

            var processIds = processes
                .Select(process => (uint)process.Id)
                .ToHashSet();

            ExplorerWindowManager.CloseWindowsForProcesses(
                processIds
            );
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static List<Process> FindProcesses(
        PaletteItem app)
    {
        var executableNames =
            GetExecutableNames(app.Path, app.Name);

        var matches = new List<Process>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var matchesExecutable =
                    executableNames.Contains(process.ProcessName);

                var title = process.MainWindowTitle;
                var matchesTitle =
                    !string.IsNullOrWhiteSpace(title) &&
                    (title.Equals(
                         app.Name,
                         StringComparison.OrdinalIgnoreCase) ||
                     title.EndsWith(
                         $" - {app.Name}",
                         StringComparison.OrdinalIgnoreCase));

                if (matchesExecutable || matchesTitle)
                {
                    matches.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        return matches;
    }

    private static HashSet<string> GetExecutableNames(
        string target,
        string appName)
    {
        var names = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        if (target.StartsWith(
                AppIndexer.ShellAppPrefix,
                StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(
                AppIndexer.ShellPathPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return names;
        }

        var executable = target;
        string? shortcutArguments = null;

        if (Path.GetExtension(target).Equals(
                ".lnk",
                StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = ResolveShortcut(target);
            executable = shortcut.Target;
            shortcutArguments = shortcut.Arguments;
        }

        if (Path.GetExtension(executable).Equals(
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            names.Add(
                Path.GetFileNameWithoutExtension(executable)
            );
        }

        AddExecutableNamesFromArguments(names, shortcutArguments);

        var compactAppName = new string(
            appName.Where(char.IsLetterOrDigit).ToArray()
        );

        if (compactAppName.Length > 1)
            names.Add(compactAppName);

        return names;
    }

    private static void AddExecutableNamesFromArguments(
        ISet<string> names,
        string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return;

        foreach (var token in arguments.Split(
                     [' ', '\t'],
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            var candidate = token.Trim('"', '\'');

            if (Path.GetExtension(candidate).Equals(
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                names.Add(Path.GetFileNameWithoutExtension(candidate));
            }
        }
    }

    private static (string Target, string Arguments) ResolveShortcut(
        string shortcutPath)
    {
        object? shellObject = null;
        object? shortcutObject = null;

        try
        {
            var shellType =
                Type.GetTypeFromProgID("WScript.Shell");

            if (shellType is null)
                return ("", "");

            shellObject = Activator.CreateInstance(shellType);

            if (shellObject is null)
                return ("", "");

            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            dynamic shortcut = shortcutObject;

            return (
                Convert.ToString(shortcut.TargetPath) ?? "",
                Convert.ToString(shortcut.Arguments) ?? ""
            );
        }
        catch
        {
            return ("", "");
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static bool IsExplorer(
        PaletteItem app,
        IReadOnlyCollection<Process> processes)
    {
        return processes.Any(process =>
                   process.ProcessName.Equals(
                       "explorer",
                       StringComparison.OrdinalIgnoreCase)) ||
               app.Name.Equals(
                   "File Explorer",
                   StringComparison.OrdinalIgnoreCase) ||
               app.Name.Equals(
                   "Windows Explorer",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void DisposeAll(
        IEnumerable<Process> processes)
    {
        foreach (var process in processes)
            process.Dispose();
    }

    private static void ReleaseComObject(
        object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
            return;

        try
        {
            Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // COM cleanup failures are non-fatal.
        }
    }
}
