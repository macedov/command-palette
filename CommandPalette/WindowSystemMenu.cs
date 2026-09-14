using System;
using System.Windows;
using System.Windows.Interop;

namespace CommandPalette;

internal static class WindowSystemMenu
{
    private const int WmSysCommand = 0x0112;
    private const int ScKeyMenu = 0xF100;

    public static void Suppress(Window window)
    {
        HwndSource? source = null;

        HwndSourceHook hook = (
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled) =>
        {
            if (message == WmSysCommand &&
                (wParam.ToInt64() & 0xFFF0) == ScKeyMenu)
            {
                handled = true;
            }

            return IntPtr.Zero;
        };

        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(
                new WindowInteropHelper(window).Handle
            );

            source?.AddHook(hook);
        };

        window.Closed += (_, _) =>
        {
            source?.RemoveHook(hook);
        };
    }
}
