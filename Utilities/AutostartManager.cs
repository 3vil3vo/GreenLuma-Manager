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
            LogService.LogError("AutostartManager.ManageAutostart", ex);
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
            LogService.LogError("AutostartManager.CleanupVbs", ex);
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
            LogService.LogError("AutostartManager.CleanupAll", ex);
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
                    LogService.LogError("AutostartManager.CleanupVbsFile", ex);
                }
        }
        catch (Exception ex)
        {
            LogService.LogError("AutostartManager.CleanupAllVbs", ex);
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
            LogService.LogError("AutostartManager.RemoveAutostart", ex);
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
            LogService.LogError("AutostartManager.RemoveMonitor", ex);
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
            LogService.LogError("AutostartManager.DeleteBackupKey", ex);
        }
    }
}