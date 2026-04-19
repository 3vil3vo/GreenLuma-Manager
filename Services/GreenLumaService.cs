using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using GreenLuma_Manager.Models;

namespace GreenLuma_Manager.Services;

public partial class GreenLumaService
{
    private const int ProcessKillTimeoutMs = 5000;
    public const int AppListLimit = 135;
    private static readonly string[] SteamProcessNames = ["steam", "steamwebhelper", "steamerrorfilereporter"];

    [GeneratedRegex(@"[A-Za-z]:\\[^""\r\n]+?\.dll", RegexOptions.IgnoreCase)]
    private static partial Regex DllPathRegex();
    [GeneratedRegex(@"GreenLuma_(\d{4})_x(64|86)\.dll", RegexOptions.IgnoreCase)]
    private static partial Regex GreenLumaDllRegex();

    public static (bool IsValid, bool IsStealthOnly, List<string> MissingFiles) ValidateInstallation(string path)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return (false, false, missing);

        string? year = null;
        string? arch = null;
        try
        {
            var dllFiles = Directory.GetFiles(path, "GreenLuma_*_x*.dll");
            foreach (var file in dllFiles)
            {
                var match = GreenLumaDllRegex().Match(Path.GetFileName(file));
                if (match.Success)
                {
                    year = match.Groups[1].Value;
                    arch = match.Groups[2].Value;
                    if (string.Equals(arch, "64", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.ValidateInstallation", ex);
        }

        if (year == null || arch == null)
        {
            missing.Add("GreenLuma_YYYY_xNN.dll (e.g. GreenLuma_2025_x64.dll)");
            return (false, false, missing);
        }

        var primaryDll = $"GreenLuma_{year}_x{arch}.dll";

        var stealthFiles = new List<string>
        {
            "DLLInjector.exe",
            "DLLInjector.ini",
            $"GreenLumaSettings_{year}.exe",
            primaryDll
        };

        var otherArch = arch == "64" ? "86" : "64";
        var fullFiles = new List<string>
        {
            $"GreenLuma_{year}_x{otherArch}.dll",
            Path.Combine($"GreenLuma{year}_Files", "AchievementUnlocked.wav"),
            Path.Combine($"GreenLuma{year}_Files", "BootImage.bmp")
        };

        var missingStealth = new List<string>();
        foreach (var f in stealthFiles)
            if (!File.Exists(Path.Combine(path, f)))
                missingStealth.Add(f);

        if (missingStealth.Count > 0)
        {
            return (false, false, missingStealth);
        }

        var missingFull = new List<string>();
        foreach (var f in fullFiles)
            if (!File.Exists(Path.Combine(path, f)))
                missingFull.Add(f);

        var x86Launcher = Path.Combine(path, "bin", "x86launcher.exe");
        var x64Launcher = Path.Combine(path, "bin", "x64launcher.exe");
        if (!File.Exists(x86Launcher) && !File.Exists(x64Launcher))
            missingFull.Add(Path.Combine("bin", "x86launcher.exe"));

        if (missingFull.Count > 0)
        {
            return (true, true, missingFull);
        }

        return (true, false, new List<string>());
    }
    public static bool IsAppListGenerated(Config config)
    {
        if (string.IsNullOrWhiteSpace(config.GreenLumaPath))
            return false;

        var appListPath = Path.Combine(config.GreenLumaPath, "AppList");

        return Directory.Exists(appListPath) &&
               Directory.GetFiles(appListPath, "*.txt").Length > 0;
    }

    public static async Task<int> GenerateAppListAsync(Profile? profile, Config? config)
    {
        if (profile == null || config == null || string.IsNullOrWhiteSpace(config.GreenLumaPath))
            return -1;

        try
        {
            var appListPath = Path.Combine(config.GreenLumaPath, "AppList");
            Directory.CreateDirectory(appListPath);

            foreach (var file in Directory.GetFiles(appListPath, "*.txt"))
                File.Delete(file);

            var allAppIds = new List<string>();

            foreach (var game in profile.Games)
            {
                allAppIds.Add(game.AppId);
                allAppIds.AddRange(game.Depots);
            }

            var totalCount = allAppIds.Count;

            var limitedAppIds = allAppIds.Take(AppListLimit).ToList();

            for (var i = 0; i < limitedAppIds.Count; i++)
            {
                var filePath = Path.Combine(appListPath, $"{i}.txt");
                await File.WriteAllTextAsync(filePath, limitedAppIds[i]);
            }

            return totalCount;
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.GenerateAppList", ex);
            return -1;
        }
    }

    public static async Task<bool> LaunchGreenLumaAsync(Config config)
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
            catch (Exception ex)
            {
                LogService.LogError("GreenLumaService.LaunchGreenLuma", ex);
                return false;
            }
        });
    }

    private static bool ValidatePaths(Config config)
    {
        if (string.IsNullOrWhiteSpace(config.SteamPath) ||
            string.IsNullOrWhiteSpace(config.GreenLumaPath))
            return false;

        var steamExePath = Path.Combine(config.SteamPath, "Steam.exe");
        var injectorPath = Path.Combine(config.GreenLumaPath, "DLLInjector.exe");

        return File.Exists(steamExePath) && File.Exists(injectorPath);
    }

    private static bool LaunchInjector(Config config)
    {
        var injectorPath = Path.Combine(config.GreenLumaPath, "DLLInjector.exe");

        if (!File.Exists(injectorPath))
            return false;

        UpdateInjectorIni(config);

        Process.Start(new ProcessStartInfo
        {
            FileName = injectorPath,
            WorkingDirectory = config.GreenLumaPath,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        return true;
    }

    private static void KillSteam(Config config)
    {
        try
        {
            var steamExePath = Path.Combine(config.SteamPath, "Steam.exe");
            string[] processNames = ["steam", "steamservice", "steamwebhelper"];

            if (File.Exists(steamExePath))
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = steamExePath,
                        Arguments = "-shutdown",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    Thread.Sleep(2000);
                }
                catch (Exception ex)
                {
                    LogService.LogError("GreenLumaService.KillSteam.Shutdown", ex);
                }

            foreach (var processName in processNames)
                KillProcessesByName(processName);
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.KillSteam", ex);
        }
    }

    private static void KillProcessesByName(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
            try
            {
                process.Kill();
                process.WaitForExit(ProcessKillTimeoutMs);
            }
            catch (Exception ex)
            {
                LogService.LogError("GreenLumaService.KillProcess", ex);
            }
    }

    private static bool AreSameDirectory(string path1, string path2)
    {
        try
        {
            var fullPath1 = Path.GetFullPath(path1)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath2 = Path.GetFullPath(path2)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath1, fullPath2, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.AreSameDirectory", ex);
            return false;
        }
    }

    private static void UpdateInjectorIni(Config config)
    {
        try
        {
            var iniPath = Path.Combine(config.GreenLumaPath, "DLLInjector.ini");

            if (!File.Exists(iniPath))
                return;

            var lines = File.ReadAllLines(iniPath).ToList();
            var dllValue = ExtractDllValue(lines);
            var settings = BuildInjectorSettings(config, dllValue);
            var updatedLines = ApplySettings(lines, settings);

            File.WriteAllLines(iniPath, updatedLines);
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.UpdateInjectorIni", ex);
        }
    }

    private static string? ExtractDllValue(List<string> lines)
    {
        try
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("Dll", StringComparison.OrdinalIgnoreCase))
                {
                    var equalsIndex = line.IndexOf('=');
                    if (equalsIndex >= 0 && equalsIndex < line.Length - 1)
                    {
                        var raw = line[(equalsIndex + 1)..].Trim();
                        var cleaned = CleanDllValue(raw);
                        return cleaned;
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.ExtractDllValue", ex);
        }

        return null;
    }

    private static string CleanDllValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var s = raw.Trim();
        s = s.Trim('"', '\'', ' ');

        try
        {
            var m = DllPathRegex().Match(s);

            if (m.Success) return m.Value;
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.CleanDllValue", ex);
        }

        return s;
    }

    private static Dictionary<string, string> BuildInjectorSettings(Config config, string? dllValue)
    {
        var useSeparatePaths = !AreSameDirectory(config.SteamPath, config.GreenLumaPath) ||
                               (!string.IsNullOrWhiteSpace(dllValue) && Path.IsPathRooted(dllValue));

        var steamExePath = Path.Combine(config.SteamPath, "Steam.exe");

        var settings = new Dictionary<string, string>
        {
            ["FileToCreate_1"] = " NoQuestion.bin"
        };

        if (useSeparatePaths)
        {
            settings["UseFullPathsFromIni"] = " 1";
            settings["Exe"] = $" \"{steamExePath}\"";

            if (!string.IsNullOrWhiteSpace(dllValue))
            {
                var candidate = dllValue.Trim();

                bool rooted;
                try
                {
                    rooted = Path.IsPathRooted(candidate);
                }
                catch (Exception ex)
                {
                    LogService.LogError("GreenLumaService.IsPathRooted", ex);
                    rooted = false;
                }

                if (rooted)
                {
                    var full = candidate;
                    try
                    {
                        full = Path.GetFullPath(candidate);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("GreenLumaService.GetFullPath", ex);
                    }

                    settings["Dll"] = $" \"{full}\"";
                }
                else
                {
                    var fullDllPath = Path.Combine(config.GreenLumaPath, candidate);
                    try
                    {
                        fullDllPath = Path.GetFullPath(fullDllPath);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("GreenLumaService.GetFullPath", ex);
                    }

                    settings["Dll"] = $" \"{fullDllPath}\"";
                }
            }
        }
        else
        {
            settings["UseFullPathsFromIni"] = " 0";
            settings["Exe"] = " Steam.exe";

            if (!string.IsNullOrWhiteSpace(dllValue)) settings["Dll"] = $" {dllValue}";
        }

        if (config.NoHook)
            ApplyStealthModeSettings(settings);
        else
            ApplyNormalModeSettings(settings);

        if (config.StartSteamMinimized)
            settings["CommandLine"] = settings.GetValueOrDefault("CommandLine", "") + " -silent";

        return settings;
    }

    private static void ApplyStealthModeSettings(Dictionary<string, string> settings)
    {
        settings["CommandLine"] = "";
        settings["WaitForProcessTermination"] = " 0";
        settings["EnableFakeParentProcess"] = " 1";
        settings["EnableMitigationsOnChildProcess"] = " 0";
        settings["CreateFiles"] = " 2";
        settings["FileToCreate_2"] = " StealthMode.bin";
    }

    private static void ApplyNormalModeSettings(Dictionary<string, string> settings)
    {
        settings["CommandLine"] = " -inhibitbootstrap";
        settings["WaitForProcessTermination"] = " 1";
        settings["EnableFakeParentProcess"] = " 0";
        settings["CreateFiles"] = " 1";
        settings.TryAdd("FileToCreate_2", "");
    }

    private static List<string> ApplySettings(List<string> originalLines, Dictionary<string, string> settings)
    {
        var result = new List<string>();

        foreach (var line in originalLines)
        {
            var trimmed = line.Trim();
            var matched = false;

            if (!string.IsNullOrWhiteSpace(trimmed) && trimmed[0] != '#' && trimmed.Contains('='))
            {
                var equalsIndex = trimmed.IndexOf('=');
                var key = trimmed[..equalsIndex].Trim();

                foreach (var setting in settings)
                    if (string.Equals(key, setting.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add($"{setting.Key}={setting.Value}");
                        matched = true;
                        break;
                    }
            }

            if (!matched) result.Add(line);
        }

        return result;
    }
}