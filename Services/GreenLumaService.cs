using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GreenLuma_Manager.Models;

namespace GreenLuma_Manager.Services;

public partial class GreenLumaService
{
    private const int ProcessKillTimeoutMs = 5000;
    public const int AppListLimit = 151;
    public const string IniAppListMinVersion = "1.8.0";
    public const string LegacyVersion = "1.7.9";
    private const string IniTemplateResourceName = "GreenLuma_Manager.Data.AppList.template.ini";

    [GeneratedRegex(@"[A-Za-z]:\\[^""\r\n]+?\.dll", RegexOptions.IgnoreCase)]
    private static partial Regex DllPathRegex();

    [GeneratedRegex(@"GreenLuma_(\d{4})_x(64|86)\.dll", RegexOptions.IgnoreCase)]
    private static partial Regex GreenLumaDllRegex();

    [GeneratedRegex(@"^#(\d+)\s*=")]
    private static partial Regex CommentedAppListLineRegex();

    [GeneratedRegex(@"^NumAppIDs\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex NumAppIdsLineRegex();

    public static int CompareVersions(string a, string b)
    {
        var partsA = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var partsB = b.Split('.', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < Math.Max(partsA.Length, partsB.Length); i++)
        {
            var va = i < partsA.Length && int.TryParse(partsA[i], out var pa) ? pa : 0;
            var vb = i < partsB.Length && int.TryParse(partsB[i], out var pb) ? pb : 0;
            if (va != vb) return va.CompareTo(vb);
        }

        return 0;
    }

    public static bool SupportsIniAppList(string? greenLumaVersion)
    {
        return !string.IsNullOrWhiteSpace(greenLumaVersion) &&
               CompareVersions(greenLumaVersion, IniAppListMinVersion) >= 0;
    }

    public static bool CanOverrideVersion(string? detectedVersion)
    {
        return string.Equals(detectedVersion, LegacyVersion, StringComparison.Ordinal) ||
               string.Equals(detectedVersion, IniAppListMinVersion, StringComparison.Ordinal);
    }

    public static string? ResolveVersion(Config config, string? detectedVersion)
    {
        if (string.IsNullOrWhiteSpace(config.GreenLumaVersionOverride))
            return detectedVersion;

        return CanOverrideVersion(detectedVersion) ? config.GreenLumaVersionOverride : detectedVersion;
    }

    public static bool ClearStaleVersionOverride(Config config, string? detectedVersion)
    {
        if (string.IsNullOrWhiteSpace(config.GreenLumaVersionOverride) || CanOverrideVersion(detectedVersion))
            return false;

        config.GreenLumaVersionOverride = string.Empty;
        return true;
    }

    public static bool RequiresNoQuestionFile(string? greenLumaVersion)
    {
        return string.IsNullOrWhiteSpace(greenLumaVersion) ||
               CompareVersions(greenLumaVersion, IniAppListMinVersion) < 0;
    }

    public static (bool IsValid, bool IsStealthOnly, List<string> MissingFiles) ValidateInstallation(string path)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return (false, false, missing);

        string? year = null;
        string? arch = null;
        try
        {
            var dllFiles = Directory.GetFiles(path, "GreenLuma_*_x*.dll")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
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
            Logger.Error(ex, "GreenLumaService.ValidateInstallation");
        }

        if (year == null || arch == null)
        {
            missing.Add("GreenLuma_YYYY_xNN.dll (e.g. GreenLuma_2026_x64.dll)");
            return (false, false, missing);
        }

        var (stealthFiles, fullFiles) = GetRequiredFileNames(year, arch);

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

    public static (List<string> StealthFiles, List<string> FullOnlyFiles) GetRequiredFileNames(
        string year, string primaryArch)
    {
        var primaryDll = $"GreenLuma_{year}_x{primaryArch}.dll";

        var stealthFiles = new List<string>
        {
            "DLLInjector.exe",
            "DLLInjector.ini",
            $"GreenLumaSettings_{year}.exe",
            primaryDll
        };

        var otherArch = primaryArch == "64" ? "86" : "64";
        var fullOnlyFiles = new List<string>
        {
            $"GreenLuma_{year}_x{otherArch}.dll",
            Path.Combine($"GreenLuma{year}_Files", "AchievementUnlocked.wav"),
            Path.Combine($"GreenLuma{year}_Files", "BootImage.bmp")
        };

        return (stealthFiles, fullOnlyFiles);
    }

    public static bool IsAppListGenerated(Config config)
    {
        var rootPath = GetRuntimeRootPath(config);
        if (string.IsNullOrWhiteSpace(rootPath))
            return false;

        var appListPath = Path.Combine(rootPath, "AppList");

        return IsAppListGenerated(appListPath);
    }

    public static bool IsAppListGenerated(string appListPath)
    {
        if (!Directory.Exists(appListPath))
            return false;

        if (Directory.GetFiles(appListPath, "*.txt").Length > 0)
            return true;

        var iniPath = Path.Combine(appListPath, "AppList.ini");
        return File.Exists(iniPath) && ReadAppIdsFromIni(iniPath).Count > 0;
    }

    public static List<string> ReadAppIdsFromIni(string iniPath)
    {
        var appIds = new List<string>();

        try
        {
            foreach (var rawLine in File.ReadLines(iniPath))
            {
                var line = rawLine.Trim();

                if (line.Length == 0 || line[0] == '#' || line[0] == '[')
                    continue;

                if (NumAppIdsLineRegex().IsMatch(line))
                    continue;

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex < 0)
                    continue;

                var value = line[(equalsIndex + 1)..].Trim();
                if (value.Length > 0)
                    appIds.Add(value);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaService.ReadAppIdsFromIni");
        }

        return appIds;
    }

    public static string? DetectVersion(string greenLumaPath)
    {
        if (string.IsNullOrWhiteSpace(greenLumaPath) || !Directory.Exists(greenLumaPath))
            return null;

        try
        {
            var dllFiles = Directory.GetFiles(greenLumaPath, "GreenLuma_*_x*.dll")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
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
            Logger.Error(ex, "GreenLumaService.DetectVersion");
            return null;
        }
    }

    public static async Task<int> GenerateAppListAsync(Profile? profile, Config? config)
    {
        if (profile == null || config == null || string.IsNullOrWhiteSpace(GetRuntimeRootPath(config)))
            return -1;

        try
        {
            var appListPath = Path.Combine(GetRuntimeRootPath(config), "AppList");
            Directory.CreateDirectory(appListPath);

            var allAppIds = new List<string>();
            var seenAppIds = new HashSet<string>();

            foreach (var game in profile.Games)
            {
                if (seenAppIds.Add(game.AppId)) allAppIds.Add(game.AppId);

                foreach (var depotId in game.Depots)
                    if (seenAppIds.Add(depotId))
                        allAppIds.Add(depotId);
            }

            var totalCount = allAppIds.Count;
            var limitedAppIds = allAppIds.Take(AppListLimit).ToList();

            if (config.LaunchMode == GreenLumaLaunchMode.FullStealth ||
                SupportsIniAppList(ResolveVersion(config, DetectVersion(config.GreenLumaPath))))
            {
                if (!await GenerateIniAppListAsync(appListPath, limitedAppIds).ConfigureAwait(false))
                    return -1;
            }
            else
            {
                await GenerateLegacyAppListAsync(appListPath, limitedAppIds).ConfigureAwait(false);
            }

            return totalCount;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaService.GenerateAppList");
            return -1;
        }
    }

    private static async Task GenerateLegacyAppListAsync(string appListPath, List<string> appIds)
    {
        DeleteAllFiles(appListPath);

        for (var i = 0; i < appIds.Count; i++)
        {
            var filePath = Path.Combine(appListPath, $"{i}.txt");
            await File.WriteAllTextAsync(filePath, appIds[i]).ConfigureAwait(false);
        }
    }

    private static async Task<bool> GenerateIniAppListAsync(string appListPath, List<string> appIds)
    {
        var templateLines = LoadIniTemplate();
        if (templateLines == null)
        {
            Logger.Error(
                new InvalidOperationException("AppList.ini template resource could not be loaded"),
                "GreenLumaService.GenerateIniAppList");
            return false;
        }

        var placeholderIndices = new List<int>();
        for (var i = 0; i < templateLines.Count; i++)
            if (CommentedAppListLineRegex().IsMatch(templateLines[i]))
                placeholderIndices.Add(i);

        var assignCount = Math.Min(appIds.Count, placeholderIndices.Count);

        for (var i = 0; i < assignCount; i++)
        {
            var lineIndex = placeholderIndices[i];
            var key = CommentedAppListLineRegex().Match(templateLines[lineIndex]).Groups[1].Value;
            templateLines[lineIndex] = $"{key} = {appIds[i]}";
        }

        for (var i = 0; i < templateLines.Count; i++)
            if (NumAppIdsLineRegex().IsMatch(templateLines[i]))
            {
                templateLines[i] = $"NumAppIDs = {assignCount}";
                break;
            }

        DeleteAllFiles(appListPath);

        var iniPath = Path.Combine(appListPath, "AppList.ini");
        await File.WriteAllLinesAsync(iniPath, templateLines).ConfigureAwait(false);
        return true;
    }

    private static void DeleteAllFiles(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath))
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "GreenLumaService.DeleteAppListFile");
            }
    }

    private static List<string>? LoadIniTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IniTemplateResourceName);
        if (stream == null) return null;

        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null) lines.Add(line);

        return lines;
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

                if (config.LaunchMode != GreenLumaLaunchMode.FullStealth)
                    return LaunchInjector(config);

                if (!GreenLumaDeploymentService.StageFullStealthForLaunch(config))
                    return false;

                if (LaunchSteam(config)) return true;
                GreenLumaDeploymentService.RemoveFullStealthDeployment(config.SteamPath);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "GreenLumaService.LaunchGreenLuma");
                return false;
            }
        });
    }

    private static bool ValidatePaths(Config config)
    {
        if (string.IsNullOrWhiteSpace(config.SteamPath) ||
            (config.LaunchMode != GreenLumaLaunchMode.FullStealth &&
             string.IsNullOrWhiteSpace(config.GreenLumaPath)))
            return false;

        var steamExePath = Path.Combine(config.SteamPath, "Steam.exe");
        if (!File.Exists(steamExePath)) return false;

        if (config.LaunchMode == GreenLumaLaunchMode.FullStealth)
        {
            var dllName = config.FullStealthVariant == FullStealthVariant.SteamFamilies
                ? "user32SF.dll"
                : "user32.dll";
            return !string.IsNullOrWhiteSpace(config.GreenLumaPath) &&
                   File.Exists(Path.Combine(config.GreenLumaPath, dllName)) &&
                   Directory.Exists(Path.Combine(config.GreenLumaPath, "AppList"));
        }

        return File.Exists(Path.Combine(config.GreenLumaPath, "DLLInjector.exe"));
    }

    private static bool LaunchSteam(Config config)
    {
        var steamExePath = Path.Combine(config.SteamPath, "Steam.exe");
        if (!File.Exists(steamExePath)) return false;

        Process.Start(new ProcessStartInfo
        {
            FileName = steamExePath,
            Arguments = config.StartSteamMinimized ? "-silent" : string.Empty,
            WorkingDirectory = config.SteamPath,
            UseShellExecute = true
        });
        return true;
    }

    public static string GetRuntimeRootPath(Config config) => config.GreenLumaPath;

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
                    Logger.Error(ex, "GreenLumaService.KillSteam.Shutdown");
                }

            foreach (var processName in processNames)
                KillProcessesByName(processName);

            WaitForProcessesExit(processNames);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaService.KillSteam");
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
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "GreenLumaService.Kill");
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
                Logger.Error(ex, "GreenLumaService.KillProcess");
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
            Logger.Error(ex, "GreenLumaService.AreSameDirectory");
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
            Logger.Error(ex, "GreenLumaService.UpdateInjectorIni");
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
            Logger.Error(ex, "GreenLumaService.ExtractDllValue");
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
            Logger.Error(ex, "GreenLumaService.CleanDllValue");
        }

        return s;
    }

    private static Dictionary<string, string> BuildInjectorSettings(Config config, string? dllValue)
    {
        var useSeparatePaths = !AreSameDirectory(config.SteamPath, config.GreenLumaPath) ||
                               (!string.IsNullOrWhiteSpace(dllValue) && Path.IsPathRooted(dllValue));

        var steamExePath = Path.Combine(config.SteamPath, "Steam.exe");

        var settings = new Dictionary<string, string>();

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
                    Logger.Error(ex, "GreenLumaService.IsPathRooted");
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
                        Logger.Error(ex, "GreenLumaService.GetFullPath");
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
                        Logger.Error(ex, "GreenLumaService.GetFullPath");
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

        var needsNoQuestionFile = RequiresNoQuestionFile(ResolveVersion(config, DetectVersion(config.GreenLumaPath)));

        if (config.NoHook)
            ApplyStealthModeSettings(settings, needsNoQuestionFile);
        else
            ApplyNormalModeSettings(settings, needsNoQuestionFile);

        if (config.StartSteamMinimized)
            settings["CommandLine"] = settings.GetValueOrDefault("CommandLine", "") + " -silent";

        return settings;
    }

    private static void ApplyStealthModeSettings(Dictionary<string, string> settings, bool needsNoQuestionFile)
    {
        settings["CommandLine"] = "";
        settings["WaitForProcessTermination"] = " 0";
        settings["EnableFakeParentProcess"] = " 1";
        settings["EnableMitigationsOnChildProcess"] = " 0";

        if (needsNoQuestionFile)
        {
            settings["CreateFiles"] = " 2";
            settings["FileToCreate_1"] = " NoQuestion.bin";
            settings["FileToCreate_2"] = " StealthMode.bin";
        }
        else
        {
            settings["CreateFiles"] = " 1";
            settings["FileToCreate_1"] = " StealthMode.bin";
            settings["FileToCreate_2"] = "";
        }
    }

    private static void ApplyNormalModeSettings(Dictionary<string, string> settings, bool needsNoQuestionFile)
    {
        settings["CommandLine"] = " -inhibitbootstrap";
        settings["WaitForProcessTermination"] = " 1";
        settings["EnableFakeParentProcess"] = " 0";

        if (needsNoQuestionFile)
        {
            settings["CreateFiles"] = " 1";
            settings["FileToCreate_1"] = " NoQuestion.bin";
        }
        else
        {
            settings["CreateFiles"] = " 0";
            settings["FileToCreate_1"] = "";
        }

        settings["FileToCreate_2"] = "";
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

        var runtimeRoot = GetRuntimeRootPath(config);
        if (string.IsNullOrWhiteSpace(runtimeRoot) || !Directory.Exists(runtimeRoot))
        {
            issues.Add(config.LaunchMode == GreenLumaLaunchMode.FullStealth
                ? "Steam path does not exist."
                : "GreenLuma path does not exist.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(config.SteamPath) || !Directory.Exists(config.SteamPath))
        {
            issues.Add("Steam path does not exist.");
            return issues;
        }

        var injectorPath = Path.Combine(config.GreenLumaPath, "DLLInjector.exe");
        if (config.LaunchMode == GreenLumaLaunchMode.FullStealth)
        {
            var dllName = config.FullStealthVariant == FullStealthVariant.SteamFamilies
                ? "user32SF.dll"
                : "user32.dll";
            if (!File.Exists(Path.Combine(config.GreenLumaPath, dllName)))
                issues.Add($"Full Stealth source file {dllName} is missing.");
            if (!Directory.Exists(Path.Combine(config.GreenLumaPath, "AppList")))
                issues.Add("Full Stealth source AppList is missing.");
        }
        else if (!File.Exists(injectorPath))
            issues.Add("DLLInjector.exe is missing. It was likely deleted by antivirus.");

        var iniPath = Path.Combine(config.GreenLumaPath, "DLLInjector.ini");
        if (config.LaunchMode == GreenLumaLaunchMode.FullStealth)
            iniPath = string.Empty;
        if (config.LaunchMode != GreenLumaLaunchMode.FullStealth && !File.Exists(iniPath))
        {
            issues.Add("DLLInjector.ini is missing.");
        }
        else if (config.LaunchMode != GreenLumaLaunchMode.FullStealth)
        {
            var dllPath = GetDllPathFromIni(iniPath, config);
            if (dllPath != null)
            {
                if (!File.Exists(dllPath))
                {
                    issues.Add(
                        $"GreenLuma DLL not found: {Path.GetFileName(dllPath)}. Antivirus may have quarantined it.");
                }
                else
                {
                    var info = new FileInfo(dllPath);
                    if (info.Length < 1024)
                        issues.Add($"GreenLuma DLL is only {info.Length} bytes. The file may be corrupted.");
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
                if (recentQuarantine != null && !File.Exists(injectorPath))
                    issues.Add($"Windows Defender quarantined a GreenLuma file. {recentQuarantine}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaService.DefenderCheck");
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
                var launchTime = DateTime.Now.AddSeconds(-5);

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
                    if (config.LaunchMode == GreenLumaLaunchMode.FullStealth)
                        return "Steam did not start in Full Stealth Mode.";
                    var injectorCrash = GetCrashFromEventLog("DLLInjector", launchTime);
                    if (injectorCrash != null)
                        return $"DLLInjector.exe crashed: {injectorCrash}";
                    return "Steam did not start. DLLInjector may have failed silently. Check your antivirus logs.";
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
                            if (config.LaunchMode == GreenLumaLaunchMode.FullStealth && crashInfo == null)
                            {
                                var replacement = WaitForSteamReplacement(10000);
                                if (replacement != null)
                                {
                                    steamProcess = replacement;
                                    continue;
                                }

                                return null;
                            }
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
                        if (config.LaunchMode == GreenLumaLaunchMode.FullStealth && crashInfo == null)
                            return null;
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
                Logger.Error(ex, "GreenLumaService.MonitorSteam");
                return $"Monitoring error: {ex.Message}";
            }
            finally
            {
                if (config.LaunchMode == GreenLumaLaunchMode.FullStealth)
                    GreenLumaDeploymentService.RemoveFullStealthDeployment(config.SteamPath);
            }
        });
    }

    private static Process? WaitForSteamReplacement(int timeoutMs)
    {
        const int pollMs = 500;
        for (var elapsed = 0; elapsed < timeoutMs; elapsed += pollMs)
        {
            Thread.Sleep(pollMs);
            var processes = Process.GetProcessesByName("steam");
            if (processes.Length == 0) continue;
            var process = processes[0];
            for (var i = 1; i < processes.Length; i++) processes[i].Dispose();
            return process;
        }

        return null;
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
            Logger.Error(ex, "GreenLumaService.GetCrashFromEventLog");
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
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaService.QuarantineCheck");
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
            Logger.Error(ex, "GreenLumaService.GetDllPathFromIni");
        }

        return null;
    }
}
