using System.IO;
using System.Text.RegularExpressions;
using GreenLuma_Manager.Services;
using Microsoft.Win32;

namespace GreenLuma_Manager.Utilities;

public partial class PathDetector
{
    [GeneratedRegex(@"GreenLuma_(\d{4})_x(64|86)\.dll", RegexOptions.IgnoreCase)]
    private static partial Regex GreenLumaDllRegex();

    public static (string SteamPath, string GreenLumaPath) DetectPaths()
    {
        var steamPath = DetectSteamPath();
        var greenLumaPath = DetectGreenLumaPath(steamPath);

        return (steamPath, greenLumaPath);
    }

    public static string DetectSteamPath()
    {
        var registryPath = TryDetectSteamFromRegistry();
        if (!string.IsNullOrEmpty(registryPath))
            return registryPath;

        return TryDetectSteamFromCommonLocations();
    }

    private static string TryDetectSteamFromRegistry()
    {
        var path = TryGetRegistryPath("SOFTWARE\\WOW6432Node\\Valve\\Steam");
        if (!string.IsNullOrEmpty(path))
            return path;

        path = TryGetRegistryPath("SOFTWARE\\Valve\\Steam");
        if (!string.IsNullOrEmpty(path))
            return path;

        return string.Empty;
    }

    private static string? TryGetRegistryPath(string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            var installPath = key?.GetValue("InstallPath") as string;

            if (!string.IsNullOrWhiteSpace(installPath) &&
                File.Exists(Path.Combine(installPath, "Steam.exe")))
                return installPath;
        }
        catch (Exception ex)
        {
            LogService.LogError("PathDetector.TryGetRegistryPath", ex);
        }

        return null;
    }

    private static string TryDetectSteamFromCommonLocations()
    {
        string[] commonPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Steam")
        ];

        foreach (var path in commonPaths)
            if (File.Exists(Path.Combine(path, "Steam.exe")))
                return path;

        return string.Empty;
    }

    public static string DetectGreenLumaPath(string steamPath)
    {
        if (!string.IsNullOrWhiteSpace(steamPath) && Directory.Exists(steamPath))
            if (ContainsGreenLumaFiles(steamPath))
                return steamPath;

        return TryDetectGreenLumaFromCommonLocations();
    }

    private static string TryDetectGreenLumaFromCommonLocations()
    {
        string[] commonPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GreenLuma"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GreenLuma"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "GreenLuma"),
            "C:\\GreenLuma",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "GreenLuma")
        ];

        foreach (var path in commonPaths)
            if (Directory.Exists(path) && ContainsGreenLumaFiles(path))
                return path;

        return string.Empty;
    }

    private static bool ContainsGreenLumaFiles(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "DLLInjector.exe")))
            return false;

        try
        {
            var files = Directory.GetFiles(directory, "GreenLuma_*_x*.dll");
            foreach (var file in files)
            {
                if (GreenLumaDllRegex().IsMatch(Path.GetFileName(file)))
                    return true;
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("PathDetector.ContainsGreenLumaFiles", ex);
        }

        return false;
    }
}