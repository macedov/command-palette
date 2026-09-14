using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CommandPalette;

internal static class ExplorerWindowManager
{
    private const uint WmClose = 0x0010;

    public static void CloseOpenWindows()
    {
        EnumWindows(
            (windowHandle, _) =>
            {
                if (!IsWindowVisible(windowHandle) ||
                    !IsFileExplorerWindow(windowHandle))
                {
                    return true;
                }

                PostMessage(
                    windowHandle,
                    WmClose,
                    IntPtr.Zero,
                    IntPtr.Zero
                );

                return true;
            },
            IntPtr.Zero
        );
    }

    public static void CloseWindowsForProcesses(
        IReadOnlySet<uint> processIds)
    {
        EnumWindows(
            (windowHandle, _) =>
            {
                if (!IsWindowVisible(windowHandle))
                    return true;

                GetWindowThreadProcessId(
                    windowHandle,
                    out var processId
                );

                if (processIds.Contains(processId))
                {
                    PostMessage(
                        windowHandle,
                        WmClose,
                        IntPtr.Zero,
                        IntPtr.Zero
                    );
                }

                return true;
            },
            IntPtr.Zero
        );
    }

    private static bool IsFileExplorerWindow(
        IntPtr windowHandle)
    {
        var className = new StringBuilder(256);

        if (GetClassName(
                windowHandle,
                className,
                className.Capacity) == 0 ||
            (!className.ToString().Equals(
                 "CabinetWClass",
                 StringComparison.Ordinal) &&
             !className.ToString().Equals(
                 "ExploreWClass",
                 StringComparison.Ordinal)))
        {
            return false;
        }

        GetWindowThreadProcessId(
            windowHandle,
            out var processId
        );

        try
        {
            using var process =
                Process.GetProcessById((int)processId);

            return process.ProcessName.Equals(
                "explorer",
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch
        {
            return false;
        }
    }

    private delegate bool EnumWindowsCallback(
        IntPtr windowHandle,
        IntPtr parameter
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr parameter
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maximumCount
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(
        IntPtr windowHandle
    );

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam
    );
}
