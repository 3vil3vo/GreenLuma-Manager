using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GreenLuma_Manager.Models;

namespace GreenLuma_Manager.Services;

public partial class GreenLumaService
{
    private const int ProcessKillTimeoutMs = 5000;
    public const int AppListLimit = 148;

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

        if (missingStealth.Count > 0) return (false, false, missingStealth);

        var missingFull = new List<string>();
        foreach (var f in fullFiles)
            if (!File.Exists(Path.Combine(path, f)))
                missingFull.Add(f);

        var x86Launcher = Path.Combine(path, "bin", "x86launcher.exe");
        var x64Launcher = Path.Combine(path, "bin", "x64launcher.exe");
        if (!File.Exists(x86Launcher) && !File.Exists(x64Launcher))
            missingFull.Add(Path.Combine("bin", "x86launcher.exe"));

        if (missingFull.Count > 0) return (true, true, missingFull);

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

    public static string? DetectVersion(string greenLumaPath)
    {
        if (string.IsNullOrWhiteSpace(greenLumaPath) || !Directory.Exists(greenLumaPath))
            return null;

        try
        {
            var dllFiles = Directory.GetFiles(greenLumaPath, "GreenLuma_*_x*.dll");
            string? primaryDll = null;

            foreach (var file in dllFiles)
            {
                var match = GreenLumaDllRegex().Match(Path.GetFileName(file));
                if (!match.Success) continue;

                primaryDll = file;
                if (string.Equals(match.Groups[2].Value, "64", StringComparison.OrdinalIgnoreCase))
                    break;
            }

            if (primaryDll == null) return null;

            var info = FileVersionInfo.GetVersionInfo(primaryDll);
            if (!string.IsNullOrWhiteSpace(info.FileVersion))
                return info.FileVersion.Trim();
            if (!string.IsNullOrWhiteSpace(info.ProductVersion))
                return info.ProductVersion.Trim();

            return null;
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.DetectVersion", ex);
            return null;
        }
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
                await File.WriteAllTextAsync(filePath, limitedAppIds[i]).ConfigureAwait(false);
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
            string[] processNames = ["steam", "steamservice", "steamwebhelper", "steamerrorfilereporter"];
            var steamExePath = Path.Combine(config.SteamPath, "Steam.exe");

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
                    Thread.Sleep(3000);
                }
                catch (Exception ex)
                {
                    LogService.LogError("GreenLumaService.KillSteam.Shutdown", ex);
                }

            foreach (var processName in processNames)
                KillProcessesByName(processName);

            WaitForProcessesExit(processNames);
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.KillSteam", ex);
        }
    }

    private static void WaitForProcessesExit(string[] processNames)
    {
        const int maxWaitMs = 10000;
        const int pollIntervalMs = 500;
        var elapsed = 0;

        while (elapsed < maxWaitMs)
        {
            var anyRunning = false;
            foreach (var name in processNames)
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    anyRunning = true;
                    foreach (var p in processes)
                        try
                        {
                            if (!p.HasExited) p.Kill();
                        }
                        catch
                        {
                        }
                        finally
                        {
                            p.Dispose();
                        }
                }
            }

            if (!anyRunning)
                break;

            Thread.Sleep(pollIntervalMs);
            elapsed += pollIntervalMs;
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

    public static List<string> RunPreLaunchDiagnostics(Config config)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(config.GreenLumaPath) || !Directory.Exists(config.GreenLumaPath))
        {
            issues.Add("GreenLuma path does not exist.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(config.SteamPath) || !Directory.Exists(config.SteamPath))
        {
            issues.Add("Steam path does not exist.");
            return issues;
        }

        var injectorPath = Path.Combine(config.GreenLumaPath, "DLLInjector.exe");
        if (!File.Exists(injectorPath))
            issues.Add("DLLInjector.exe is missing — likely deleted by antivirus.");

        var iniPath = Path.Combine(config.GreenLumaPath, "DLLInjector.ini");
        if (!File.Exists(iniPath))
        {
            issues.Add("DLLInjector.ini is missing.");
        }
        else
        {
            var dllPath = GetDllPathFromIni(iniPath, config);
            if (dllPath != null)
            {
                if (!File.Exists(dllPath))
                {
                    issues.Add(
                        $"GreenLuma DLL not found: {Path.GetFileName(dllPath)} — likely quarantined by antivirus.");
                }
                else
                {
                    var info = new FileInfo(dllPath);
                    if (info.Length < 1024)
                        issues.Add($"GreenLuma DLL is only {info.Length} bytes — possibly corrupted.");
                }
            }
        }

        var steamExe = Path.Combine(config.SteamPath, "Steam.exe");
        if (!File.Exists(steamExe))
            issues.Add("Steam.exe not found at configured Steam path.");

        var steamProcs = Process.GetProcessesByName("steam");
        if (steamProcs.Length > 0)
            foreach (var p in steamProcs)
                p.Dispose();

        try
        {
            var defenderLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Support");
            if (Directory.Exists(defenderLog))
            {
                var glPath = config.GreenLumaPath.ToLowerInvariant();
                var recentQuarantine = CheckRecentDefenderDetections(glPath);
                if (recentQuarantine != null)
                    issues.Add($"Windows Defender recently quarantined: {recentQuarantine}");
            }
        }
        catch
        {
        }

        var conflictingFiles = new[] { "RTSSHooks64.dll", "RTSSHooks.dll" };
        foreach (var f in conflictingFiles)
            if (File.Exists(Path.Combine(config.SteamPath, f)))
                issues.Add($"Conflicting overlay detected: {f} (RivaTuner/MSI Afterburner).");

        return issues;
    }

    public static async Task<string?> MonitorSteamAfterLaunchAsync(Config config, int timeoutSeconds = 30)
    {
        return await Task.Run(() =>
        {
            try
            {
                var launchTime = DateTime.Now;

                Process? steamProcess = null;
                var waitMs = 0;
                const int pollMs = 500;
                const int maxWaitForStartMs = 15000;

                while (waitMs < maxWaitForStartMs)
                {
                    Thread.Sleep(pollMs);
                    waitMs += pollMs;

                    var procs = Process.GetProcessesByName("steam");
                    if (procs.Length > 0)
                    {
                        steamProcess = procs[0];
                        for (var i = 1; i < procs.Length; i++) procs[i].Dispose();
                        break;
                    }
                }

                if (steamProcess == null)
                {
                    var injectorCrash = GetCrashFromEventLog("DLLInjector", launchTime);
                    if (injectorCrash != null)
                        return $"DLLInjector.exe crashed: {injectorCrash}";
                    return "Steam process never started — DLLInjector may have failed silently. Check antivirus logs.";
                }

                var remainingMs = timeoutSeconds * 1000 - waitMs;
                if (remainingMs < 5000) remainingMs = 5000;
                var elapsed = 0;

                while (elapsed < remainingMs)
                {
                    Thread.Sleep(pollMs);
                    elapsed += pollMs;

                    try
                    {
                        steamProcess.Refresh();
                        if (steamProcess.HasExited)
                        {
                            int exitCode;
                            try
                            {
                                exitCode = steamProcess.ExitCode;
                            }
                            catch
                            {
                                exitCode = -1;
                            }

                            steamProcess.Dispose();

                            var crashInfo = GetCrashFromEventLog("steam", launchTime);
                            var sb = new StringBuilder();
                            sb.Append($"Steam exited prematurely (exit code: {exitCode}).");

                            if (crashInfo != null)
                                sb.Append($"\n\nCrash details from Event Log:\n{crashInfo}");
                            else
                                sb.Append("\n\nNo crash details found in Event Log. Possible causes:\n" +
                                          "• Antivirus blocked the DLL injection\n" +
                                          "• GreenLuma DLL is incompatible with current Steam version\n" +
                                          "• Steam client beta has breaking changes");

                            return sb.ToString();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        steamProcess.Dispose();
                        var crashInfo = GetCrashFromEventLog("steam", launchTime);
                        return crashInfo != null
                            ? $"Steam crashed.\n\nCrash details from Event Log:\n{crashInfo}"
                            : "Steam process disappeared unexpectedly.";
                    }
                }

                steamProcess.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                LogService.LogError("GreenLumaService.MonitorSteam", ex);
                return $"Monitoring error: {ex.Message}";
            }
        });
    }

    private static string? GetCrashFromEventLog(string processName, DateTime since)
    {
        try
        {
            var query = new EventLogQuery("Application", PathType.LogName,
                $"*[System[(EventID=1000 or EventID=1002) and TimeCreated[@SystemTime>='{since.ToUniversalTime():o}']]]");

            using var reader = new EventLogReader(query);

            EventRecord? record;
            while ((record = reader.ReadEvent()) != null)
                using (record)
                {
                    var desc = record.FormatDescription();
                    if (desc == null) continue;

                    if (desc.Contains(processName, StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("steam", StringComparison.OrdinalIgnoreCase))
                    {
                        var lines = desc.Split('\n');
                        var sb = new StringBuilder();
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("Faulting application", StringComparison.OrdinalIgnoreCase) ||
                                trimmed.StartsWith("Faulting module", StringComparison.OrdinalIgnoreCase) ||
                                trimmed.StartsWith("Exception code", StringComparison.OrdinalIgnoreCase) ||
                                trimmed.StartsWith("Fault offset", StringComparison.OrdinalIgnoreCase))
                                sb.AppendLine(trimmed);
                        }

                        if (sb.Length > 0)
                            return sb.ToString().TrimEnd();
                    }
                }
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.GetCrashFromEventLog", ex);
        }

        return null;
    }

    private static string? CheckRecentDefenderDetections(string greenLumaPathLower)
    {
        try
        {
            var query = new EventLogQuery(
                "Microsoft-Windows-Windows Defender/Operational",
                PathType.LogName,
                "*[System[(EventID=1116 or EventID=1117) and TimeCreated[timediff(@SystemTime) <= 86400000]]]");

            using var reader = new EventLogReader(query);
            EventRecord? record;
            while ((record = reader.ReadEvent()) != null)
                using (record)
                {
                    var desc = record.FormatDescription();
                    if (desc != null && desc.ToLowerInvariant().Contains(greenLumaPathLower))
                    {
                        var lines = desc.Split('\n');
                        foreach (var line in lines)
                            if (line.Contains("file:", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("path:", StringComparison.OrdinalIgnoreCase))
                                return line.Trim();

                        return "GreenLuma file quarantined (check Windows Security > Protection History)";
                    }
                }
        }
        catch
        {
        }

        return null;
    }

    private static string? GetDllPathFromIni(string iniPath, Config config)
    {
        try
        {
            var lines = File.ReadAllLines(iniPath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Dll", StringComparison.OrdinalIgnoreCase))
                {
                    var eq = trimmed.IndexOf('=');
                    if (eq < 0 || eq >= trimmed.Length - 1) continue;

                    var raw = trimmed[(eq + 1)..].Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    if (Path.IsPathRooted(raw))
                        return raw;

                    return Path.Combine(config.GreenLumaPath, raw);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("GreenLumaService.GetDllPathFromIni", ex);
        }

        return null;
    }
}