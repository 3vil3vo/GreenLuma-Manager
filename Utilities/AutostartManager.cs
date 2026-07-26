using System.IO;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;
using Microsoft.Win32;

namespace GreenLuma_Manager.Utilities;

public class AutostartManager
{
    private const string RunKeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string BackupKeyPath = "SOFTWARE\\GLM_Manager";
    private const string GreenLumaValueName = "GreenLumaManager";
    private const string GreenLumaMonitorValueName = "GreenLumaMonitor";

    public static void ManageAutostart(bool replaceSteam, Config? config)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);

            if (runKey == null)
                return;

            if (replaceSteam && !string.IsNullOrWhiteSpace(config?.GreenLumaPath))
                ReplaceWithGreenLuma(runKey, config);
            else
                RestoreOriginalSteam(runKey, config?.GreenLumaPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AutostartManager.ManageAutostart");
        }
    }

    private static void ReplaceWithGreenLuma(RegistryKey runKey, Config? config)
    {
        if (config == null)
            return;

        var appPath = Environment.ProcessPath ??
                      Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);

        if (string.IsNullOrWhiteSpace(appPath))
            return;

        runKey.SetValue(GreenLumaMonitorValueName, $"\"{appPath}\" --launch-greenluma");
    }

    private static void RestoreOriginalSteam(RegistryKey runKey, string? greenlumaPath)
    {
        runKey.DeleteValue(GreenLumaMonitorValueName, false);
        CleanupVbsScript(greenlumaPath);
    }

    private static void CleanupVbsScript(string? greenlumaPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(greenlumaPath))
            {
                var vbsPath = Path.Combine(greenlumaPath, "GLM_Autostart.vbs");
                if (File.Exists(vbsPath)) File.Delete(vbsPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AutostartManager.CleanupVbs");
        }
    }

    public static void CleanupAll()
    {
        try
        {
            RemoveGreenLumaAutostart();
            RemoveGreenLumaMonitor();
            DeleteBackupKey();
            CleanupAllVbsScripts();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AutostartManager.CleanupAll");
        }
    }

    private static void CleanupAllVbsScripts()
    {
        try
        {
            string[] commonPaths =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Steam"),
                "C:\\GreenLuma"
            ];

            foreach (var basePath in commonPaths)
                try
                {
                    var vbsPath = Path.Combine(basePath, "GLM_Autostart.vbs");
                    if (File.Exists(vbsPath)) File.Delete(vbsPath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "AutostartManager.CleanupVbsFile");
                }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AutostartManager.CleanupAllVbs");
        }
    }

    private static void RemoveGreenLumaAutostart()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            runKey?.DeleteValue(GreenLumaValueName, false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AutostartManager.RemoveAutostart");
        }
    }

    private static void RemoveGreenLumaMonitor()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            runKey?.DeleteValue(GreenLumaMonitorValueName, false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AutostartManager.RemoveMonitor");
        }
    }

    private static void DeleteBackupKey()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(BackupKeyPath, false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AutostartManager.DeleteBackupKey");
        }
    }
}