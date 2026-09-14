using System;
using System.IO;

namespace CommandPalette;

internal static class ConfigFileRecovery
{
    public static string DescribeFailure(
        string configPath,
        Exception exception)
    {
        var backupPath = TryBackUp(configPath);

        return backupPath is null
            ? exception.Message
            : $"{exception.Message} A copy was preserved at '{backupPath}'.";
    }

    private static string? TryBackUp(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
                return null;

            var backupPath = configPath + ".invalid.bak";
            File.Copy(configPath, backupPath, overwrite: true);
            return backupPath;
        }
        catch
        {
            return null;
        }
    }
}
