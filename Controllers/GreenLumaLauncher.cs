using System.Diagnostics;
using System.IO;
using GreenLuma_Manager.Models;

namespace GreenLuma_Manager.Controllers;

public class GreenLumaLauncher
{
    public bool ValidatePaths(Config config)
    {
        if (string.IsNullOrWhiteSpace(config.GreenLumaPath))
        {
            return false;
        }

        if (!Directory.Exists(config.GreenLumaPath))
        {
            return false;
        }

        return true;
    }

    public bool IsAppListGenerated(Config config)
    {
        if (string.IsNullOrWhiteSpace(config.GreenLumaPath))
            return false;

        var appListPath = Path.Combine(config.GreenLumaPath, "AppList");

        return Directory.Exists(appListPath) &&
               Directory.GetFiles(appListPath, "*.txt").Length > 0;
    }

    public async Task<bool> LaunchAsync(Config config)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!ValidatePaths(config))
                    return false;

                KillSteam(config);

                return LaunchInjector(config);
            }
            catch
            {
                return false;
            }
        });
    }

    private static void KillSteam(Config config)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("Steam"))
            {
                if (string.IsNullOrWhiteSpace(config.SteamPath))
                {
                    process.Kill();
                    continue;
                }

                try
                {
                    if (process.MainModule?.FileName != null &&
                        process.MainModule.FileName.StartsWith(config.SteamPath, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                    }
                }
                catch
                {
                    process.Kill();
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    private static bool LaunchInjector(Config config)
    {
        try
        {
            var greenLumaPath = config.GreenLumaPath;
            if (string.IsNullOrWhiteSpace(greenLumaPath))
                return false;

            var noHook = config.NoHook;

            var x86Launcher = Path.Combine(greenLumaPath, "bin", "x86launcher.exe");
            var x64Launcher = Path.Combine(greenLumaPath, "bin", "x64launcher.exe");
            var launcherPath = File.Exists(x64Launcher) ? x64Launcher : x86Launcher;

            if (!File.Exists(launcherPath))
            {
                var appListPath = Path.Combine(greenLumaPath, "AppList");
                var dllFiles = Directory.GetFiles(greenLumaPath, "GreenLuma_*.dll");
                if (dllFiles.Length == 0 && !Directory.Exists(appListPath))
                    return false;

                return InjectDirectly(greenLumaPath, noHook);
            }

            return RunLauncher(launcherPath, noHook);
        }
        catch
        {
            return false;
        }
    }

    private static bool InjectDirectly(string greenLumaPath, bool noHook)
    {
        try
        {
            var steamPath = Path.Combine(greenLumaPath, "Steam.exe");
            if (!File.Exists(steamPath))
            {
                steamPath = Path.Combine(greenLumaPath, "steamapps", "Steam.exe");
                if (!File.Exists(steamPath))
                    return false;
            }

            var args = noHook ? "-nohook" : "";
            Process.Start(new ProcessStartInfo
            {
                FileName = steamPath,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(steamPath) ?? greenLumaPath,
                UseShellExecute = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool RunLauncher(string launcherPath, bool noHook)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? string.Empty,
                UseShellExecute = true
            };

            if (noHook)
                startInfo.Arguments = "-nohook";

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
