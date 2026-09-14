using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CommandPalette;

public static class SystemCommandService
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const uint EwxLogoff = 0x00000000;
    private const uint EwxShutdown = 0x00000001;
    private const uint EwxReboot = 0x00000002;
    private const uint EwxForceIfHung = 0x00000010;

    public static IReadOnlyList<PaletteItem> Items { get; } =
    [
        Create("Terminal", "terminal", "windows terminal wt console"),
        Create("PowerShell", "powershell", "powershell terminal console"),
        Create("Command Prompt", "cmd", "cmd command prompt terminal console"),
        Create("Lock PC", "lock", "lock computer workstation"),
        Create("Sleep", "sleep", "sleep suspend computer"),
        Create("Hibernate", "hibernate", "hibernate computer"),
        Create("Sign out", "sign-out", "sign out logoff windows"),
        Create("Restart", "restart", "restart reboot computer"),
        Create("Shut down", "shutdown", "shutdown shut down power off computer")
    ];

    public static bool IsDestructive(
        string commandId)
    {
        return commandId is "sign-out" or "restart" or "shutdown";
    }

    public static bool TryExecute(
        string commandId,
        out string? error)
    {
        try
        {
            switch (commandId)
            {
                case "terminal":
                    return TerminalLauncher.TryOpen(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile
                        ),
                        out error
                    );

                case "powershell":
                    Start("powershell.exe");
                    break;

                case "cmd":
                    Start("cmd.exe");
                    break;

                case "lock":
                    EnsureSucceeded(LockWorkStation());
                    break;

                case "sleep":
                    EnableShutdownPrivilege();
                    EnsureSucceeded(SetSuspendState(false, false, false));
                    break;

                case "hibernate":
                    EnableShutdownPrivilege();
                    EnsureSucceeded(SetSuspendState(true, false, false));
                    break;

                case "sign-out":
                    EnsureSucceeded(ExitWindowsEx(EwxLogoff, 0));
                    break;

                case "restart":
                    EnableShutdownPrivilege();
                    EnsureSucceeded(
                        ExitWindowsEx(EwxReboot | EwxForceIfHung, 0)
                    );
                    break;

                case "shutdown":
                    EnableShutdownPrivilege();
                    EnsureSucceeded(
                        ExitWindowsEx(EwxShutdown | EwxForceIfHung, 0)
                    );
                    break;

                default:
                    error = $"Unknown system command: {commandId}.";
                    return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static PaletteItem Create(
        string name,
        string commandId,
        string keywords)
    {
        return new PaletteItem(
            name,
            commandId,
            PaletteItemType.SystemCommand,
            "System command",
            keywords
        );
    }

    private static void Start(string executable)
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true
            }
        );
    }

    private static void EnsureSucceeded(bool succeeded)
    {
        if (!succeeded)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void EnableShutdownPrivilege()
    {
        using var currentProcess = Process.GetCurrentProcess();

        if (!OpenProcessToken(
                currentProcess.Handle,
                TokenAdjustPrivileges | TokenQuery,
                out var tokenHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            if (!LookupPrivilegeValue(
                    null,
                    "SeShutdownPrivilege",
                    out var luid))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled
            };

            if (!AdjustTokenPrivileges(
                    tokenHandle,
                    false,
                    ref privileges,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            const int ErrorNotAllAssigned = 1300;

            if (Marshal.GetLastWin32Error() == ErrorNotAllAssigned)
            {
                throw new Win32Exception(ErrorNotAllAssigned);
            }
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ExitWindowsEx(
        uint flags,
        uint reason
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle
    );

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(
        string? systemName,
        string name,
        out Luid luid
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
