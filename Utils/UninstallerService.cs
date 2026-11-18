using System;
using System.Diagnostics;
using System.IO;
using MicCompressor.Localization;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MicCompressor.Utils;

public static class UninstallerService
{
    public static void RequestFullUninstall()
    {
        var loc = LocalizationService.Instance;
        var confirm = MessageBox.Show(
            loc["Uninstall_Confirm_Message"],
            loc["Uninstall_Confirm_Title"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        ExecuteFullUninstall();
    }

    public static void ExecuteFullUninstall()
    {
        // 1. Remove registry autostart entries
        AutoStartHelper.RemoveAllAutoStart();

        // 2. Remove configuration directories in %APPDATA%
        string appData = UninstallHelper.GetConfigDirectory();
        string legacyAppData = UninstallHelper.GetLegacyConfigDirectory();
        UninstallHelper.CleanDirectories(appData, legacyAppData);

        // 3. Launch self-delete script for the running executable
        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = UninstallHelper.BuildSelfDeleteCommandLine(exePath, appData, legacyAppData),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch { }
        }

        // 4. Close windows and terminate application
        try
        {
            var app = Application.Current;
            if (app != null)
            {
                foreach (System.Windows.Window window in app.Windows)
                {
                    try { window.Close(); } catch { }
                }
                app.Shutdown();
            }
        }
        catch { }

        Environment.Exit(0);
    }
}
