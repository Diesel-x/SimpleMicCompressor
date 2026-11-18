using System;
using System.IO;

namespace MicCompressor.Utils;

public static class UninstallHelper
{
    public static string GetConfigDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleMicCompressor");
    }

    public static string GetLegacyConfigDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicCompressor");
    }

    public static bool CleanDirectories(string? targetDir = null, string? legacyDir = null)
    {
        bool anyCleaned = false;
        string dir1 = targetDir ?? GetConfigDirectory();
        string dir2 = legacyDir ?? GetLegacyConfigDirectory();

        try
        {
            if (Directory.Exists(dir1))
            {
                Directory.Delete(dir1, true);
                anyCleaned = true;
            }
        }
        catch { }

        try
        {
            if (Directory.Exists(dir2))
            {
                Directory.Delete(dir2, true);
                anyCleaned = true;
            }
        }
        catch { }

        return anyCleaned;
    }

    public static string BuildSelfDeleteCommandLine(string exePath, string appDataDir, string legacyAppDataDir)
    {
        return $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{exePath}\" & rmdir /s /q \"{appDataDir}\" & rmdir /s /q \"{legacyAppDataDir}\"";
    }
}
