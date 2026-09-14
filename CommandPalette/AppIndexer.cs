using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CommandPalette;

public static class AppIndexer
{
    // Internal targets resolved by MainWindow.OpenTarget().
    public const string ShellAppPrefix = "shellapp:";
    public const string ShellPathPrefix = "shellpath:";

    public static Task<List<PaletteItem>> IndexAsync(
        IEnumerable<CustomApplication>? customApps = null)
    {
        var customAppsCopy =
            (customApps ?? [])
                .Select(app => new CustomApplication
                {
                    Name = app.Name,
                    Path = app.Path
                })
                .ToList();

        var completion =
            new TaskCompletionSource<List<PaletteItem>>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

        // Shell.Application requires STA, so discovery runs on a dedicated
        // thread instead of blocking the UI thread.
        var thread = new Thread(() =>
        {
            try
            {
                var results = new List<PaletteItem>();

                // Deduplication keeps the first match, so source order
                // preserves custom apps and shortcuts over fallback sources.
                IndexCustomApplications(results, customAppsCopy);
                IndexShortcutFolders(results);
                IndexRegisteredAppPaths(results);
                IndexInstalledApplications(results);
                IndexShellApps(results);

                var finalResults =
                    results
                        .Where(item =>
                            !string.IsNullOrWhiteSpace(item.Name) &&
                            !string.IsNullOrWhiteSpace(item.Path))
                        .GroupBy(
                            item => item.Name.Trim(),
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .OrderBy(
                            item => item.Name,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();

                Debug.WriteLine(
                    $"AppIndexer found {finalResults.Count} apps."
                );

                completion.SetResult(finalResults);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        thread.Name = "Command Palette app indexer";
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }

    private static void IndexCustomApplications(
        List<PaletteItem> results,
        IEnumerable<CustomApplication> customApps)
    {
        foreach (var app in customApps)
        {
            var path = SettingsManager.ExpandPath(app.Path);

            if (string.IsNullOrWhiteSpace(app.Name) ||
                !File.Exists(path))
            {
                continue;
            }

            results.Add(
                new PaletteItem(
                    app.Name.Trim(),
                    path,
                    PaletteItemType.App,
                    "Custom application"
                )
            );
        }
    }

    private static void IndexShortcutFolders(
        List<PaletteItem> results)
    {
        var folders = new[]
        {
            Environment.GetFolderPath(
                Environment.SpecialFolder.Programs),

            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonPrograms),

            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory),

            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) ||
                !Directory.Exists(folder))
            {
                continue;
            }

            IndexShortcutFolder(
                folder,
                results
            );
        }
    }

    private static void IndexShortcutFolder(
        string root,
        List<PaletteItem> results)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            try
            {
                foreach (var file in
                         Directory.EnumerateFiles(current))
                {
                    var extension =
                        Path.GetExtension(file);

                    if (!extension.Equals(
                            ".lnk",
                            StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(
                            ".appref-ms",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name =
                        Path.GetFileNameWithoutExtension(file);

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    results.Add(
                        new PaletteItem(
                            name,
                            file,
                            PaletteItemType.App,
                            "Application"
                        )
                    );
                }
            }
            catch
            {
                // Skip inaccessible files without aborting discovery.
            }

            try
            {
                foreach (var directory in
                         Directory.EnumerateDirectories(current))
                {
                    stack.Push(directory);
                }
            }
            catch
            {
                // Skip inaccessible directories without aborting discovery.
            }
        }
    }

    private static void IndexRegisteredAppPaths(
        List<PaletteItem> results)
    {
        const string appPathsKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

        var locations = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32)
        };

        foreach (var (hive, view) in locations)
        {
            try
            {
                using var baseKey =
                    RegistryKey.OpenBaseKey(
                        hive,
                        view
                    );

                using var appPaths =
                    baseKey.OpenSubKey(
                        appPathsKey
                    );

                if (appPaths is null)
                    continue;

                foreach (var subKeyName in
                         appPaths.GetSubKeyNames())
                {
                    try
                    {
                        using var appKey =
                            appPaths.OpenSubKey(
                                subKeyName
                            );

                        if (appKey?.GetValue(null)
                            is not string rawPath)
                        {
                            continue;
                        }

                        var executable =
                            NormalizeExecutablePath(
                                rawPath
                            );

                        if (string.IsNullOrWhiteSpace(executable) ||
                            !File.Exists(executable))
                        {
                            continue;
                        }

                        var name =
                            Path.GetFileNameWithoutExtension(
                                subKeyName
                            );

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name =
                                Path.GetFileNameWithoutExtension(
                                    executable
                                );
                        }

                        results.Add(
                            new PaletteItem(
                                name,
                                executable,
                                PaletteItemType.App,
                                "Application"
                            )
                        );
                    }
                    catch
                    {
                        // Skip malformed registry entries.
                    }
                }
            }
            catch
            {
                // A registry view may be unavailable or inaccessible.
            }
        }
    }

    private static string NormalizeExecutablePath(
        string rawPath)
    {
        var path =
            Environment.ExpandEnvironmentVariables(
                rawPath.Trim()
            );

        if (path.Length >= 2 &&
            path[0] == '"')
        {
            var closingQuote =
                path.IndexOf('"', 1);

            if (closingQuote > 1)
            {
                path =
                    path[1..closingQuote];
            }
        }
        else
        {
            path =
                path.Trim('"');
        }

        return path;
    }

    // Use explicit uninstall entries instead of scanning Program Files,
    // which would surface helpers and uninstallers.
    private static void IndexInstalledApplications(
        List<PaletteItem> results)
    {
        const string uninstallKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        var locations = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32)
        };

        foreach (var (hive, view) in locations)
        {
            try
            {
                using var baseKey =
                    RegistryKey.OpenBaseKey(hive, view);

                using var installedApps =
                    baseKey.OpenSubKey(uninstallKey);

                if (installedApps is null)
                    continue;

                foreach (var subKeyName in
                         installedApps.GetSubKeyNames())
                {
                    try
                    {
                        using var appKey =
                            installedApps.OpenSubKey(subKeyName);

                        if (appKey is null ||
                            IsHiddenInstalledEntry(appKey) ||
                            appKey.GetValue("DisplayName")
                                is not string displayName ||
                            appKey.GetValue("DisplayIcon")
                                is not string displayIcon)
                        {
                            continue;
                        }

                        var executable =
                            NormalizeDisplayIconPath(displayIcon);

                        if (!IsApplicationExecutable(executable))
                            continue;

                        results.Add(
                            new PaletteItem(
                                displayName.Trim(),
                                executable!,
                                PaletteItemType.App,
                                "Application"
                            )
                        );
                    }
                    catch
                    {
                        // Skip incomplete registry entries.
                    }
                }
            }
            catch
            {
                // A registry view may be unavailable or inaccessible.
            }
        }
    }

    private static bool IsHiddenInstalledEntry(
        RegistryKey appKey)
    {
        if (Convert.ToInt32(
                appKey.GetValue("SystemComponent", 0)) == 1)
        {
            return true;
        }

        if (appKey.GetValue("ParentKeyName") is not null)
            return true;

        var releaseType =
            Convert.ToString(appKey.GetValue("ReleaseType")) ?? "";

        return releaseType.Contains(
                   "Update",
                   StringComparison.OrdinalIgnoreCase) ||
               releaseType.Contains(
                   "Hotfix",
                   StringComparison.OrdinalIgnoreCase) ||
               releaseType.Contains(
                   "Security",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeDisplayIconPath(
        string rawValue)
    {
        var path =
            Environment.ExpandEnvironmentVariables(
                rawValue.Trim()
            );

        if (path.StartsWith('"'))
        {
            var closingQuote = path.IndexOf('"', 1);

            path = closingQuote > 1
                ? path[1..closingQuote]
                : path.Trim('"');
        }
        else
        {
            var iconIndex = path.LastIndexOf(',');

            if (iconIndex > 0 &&
                int.TryParse(
                    path[(iconIndex + 1)..],
                    out _))
            {
                path = path[..iconIndex];
            }
        }

        return path.Trim();
    }

    private static bool IsApplicationExecutable(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.GetExtension(path).Equals(
                ".exe",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return false;
        }

        var fileName =
            Path.GetFileNameWithoutExtension(path);

        var rejectedNames = new[]
        {
            "unins",
            "uninstall",
            "setup",
            "installer",
            "update",
            "updater",
            "crash",
            "helper",
            "report",
            "repair",
            "maintenancetool"
        };

        return !rejectedNames.Any(rejected =>
            fileName.StartsWith(
                rejected,
                StringComparison.OrdinalIgnoreCase
            ));
    }

    // AppsFolder covers Store/MSIX apps that may not expose executable paths.
    private static void IndexShellApps(
        List<PaletteItem> results)
    {
        object? shellObject = null;
        object? folderObject = null;
        object? itemsObject = null;

        try
        {
            var shellType =
                Type.GetTypeFromProgID(
                    "Shell.Application"
                );

            if (shellType is null)
                return;

            shellObject =
                Activator.CreateInstance(
                    shellType
                );

            if (shellObject is null)
                return;

            dynamic shell = shellObject;

            folderObject =
                shell.NameSpace(
                    "shell:AppsFolder"
                );

            if (folderObject is null)
                return;

            dynamic folder = folderObject;

            itemsObject =
                folder.Items();

            if (itemsObject is null)
                return;

            dynamic items = itemsObject;

            int count = items.Count;

            for (var i = 0; i < count; i++)
            {
                object? itemObject = null;

                try
                {
                    itemObject =
                        items.Item(i);

                    if (itemObject is null)
                        continue;

                    dynamic item = itemObject;

                    string name =
                        Convert.ToString(
                            item.Name
                        ) ?? "";

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    string? appUserModelId = null;

                    try
                    {
                        appUserModelId =
                            Convert.ToString(
                                item.ExtendedProperty(
                                    "System.AppUserModel.ID"
                                )
                            );
                    }
                    catch
                    {
                        // Not every shell item exposes an AppUserModelId.
                    }

                    string target;

                    if (!string.IsNullOrWhiteSpace(
                            appUserModelId))
                    {
                        target =
                            ShellAppPrefix +
                            appUserModelId;
                    }
                    else
                    {
                        string path =
                            Convert.ToString(
                                item.Path
                            ) ?? "";

                        if (string.IsNullOrWhiteSpace(path))
                            continue;

                        target =
                            path.StartsWith(
                                "shell:",
                                StringComparison.OrdinalIgnoreCase)
                                ? ShellPathPrefix + path
                                : path;
                    }

                    results.Add(
                        new PaletteItem(
                            name,
                            target,
                            PaletteItemType.App,
                            "Application"
                        )
                    );
                }
                catch
                {
                    // Skip malformed shell items.
                }
                finally
                {
                    ReleaseComObject(
                        itemObject
                    );
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"AppsFolder indexing failed: {ex}"
            );
        }
        finally
        {
            ReleaseComObject(
                itemsObject
            );

            ReleaseComObject(
                folderObject
            );

            ReleaseComObject(
                shellObject
            );
        }
    }

    private static void ReleaseComObject(
        object? value)
    {
        if (value is null ||
            !Marshal.IsComObject(value))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(
                value
            );
        }
        catch
        {
            // COM cleanup failures are non-fatal.
        }
    }
}
