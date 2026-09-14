using System.Collections.Generic;

namespace CommandPalette;

public static class WindowsSettingsCatalog
{
    public static IReadOnlyList<PaletteItem> Items { get; } =
    [
        Create("Bluetooth & devices", "ms-settings:bluetooth", "bluetooth devices"),
        Create("Sound", "ms-settings:sound", "sound audio volume microphone speakers"),
        Create("Display", "ms-settings:display", "display screen monitor resolution"),
        Create("Wi-Fi", "ms-settings:network-wifi", "wifi wireless network"),
        Create("Network & internet", "ms-settings:network", "network internet ethernet vpn"),
        Create("Installed apps", "ms-settings:appsfeatures", "apps applications uninstall"),
        Create("Startup apps", "ms-settings:startupapps", "startup apps login"),
        Create("Notifications", "ms-settings:notifications", "notifications alerts"),
        Create("Storage", "ms-settings:storagesense", "storage disk space"),
        Create("Privacy & security", "ms-settings:privacy", "privacy security permissions"),
        Create("Windows Update", "ms-settings:windowsupdate", "windows update updates")
    ];

    private static PaletteItem Create(
        string name,
        string target,
        string keywords)
    {
        return new PaletteItem(
            name,
            target,
            PaletteItemType.WindowsSetting,
            "Windows Settings",
            keywords
        );
    }
}
