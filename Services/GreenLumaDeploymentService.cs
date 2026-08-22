using System.IO;
using System.Text.RegularExpressions;
using GreenLuma_Manager.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace GreenLuma_Manager.Services;

public static partial class GreenLumaDeploymentService
{
    private const string ArchivePassword = "cs.rin.ru";
    private const string FullStealthMarkerName = ".glm-full-stealth";
    private const string User32BackupName = "user32.dll.glm-backup";
    private const string AppListBackupName = "AppList.glm-backup";

    [GeneratedRegex(@"GreenLuma_(\d{4})_", RegexOptions.IgnoreCase)]
    private static partial Regex YearRegex();

    public static Task<GreenLumaDeploymentResult> DeployAsync(
        string archivePath,
        string destinationPath,
        GreenLumaLaunchMode mode,
        FullStealthVariant fullStealthVariant,
        Action<string>? progressCallback = null)
    {
        return Task.Run(() => Deploy(archivePath, destinationPath, mode, fullStealthVariant, progressCallback));
    }

    private static GreenLumaDeploymentResult Deploy(
        string archivePath, string destinationPath, GreenLumaLaunchMode mode,
        FullStealthVariant fullStealthVariant, Action<string>? progressCallback)
    {
        var result = new GreenLumaDeploymentResult();
        var tempDir = Path.Combine(Path.GetTempPath(), "GLM_Deploy_" + Guid.NewGuid().ToString("N"));

        try
        {
            if (!File.Exists(archivePath))
            {
                result.ErrorMessage = $"Archive not found: {archivePath}";
                return result;
            }

            progressCallback?.Invoke("Extracting archive...");
            Directory.CreateDirectory(tempDir);

            if (!TryExtractArchive(archivePath, tempDir, out var extractError))
            {
                result.ErrorMessage = extractError;
                return result;
            }

            var sourceDir = FindSourceDirectory(tempDir);

            if (mode == GreenLumaLaunchMode.FullStealth)
                return DeployFullStealth(sourceDir, destinationPath, fullStealthVariant, result, progressCallback);

            var availableFiles = CollectAvailableFiles(sourceDir);
            var year = DetectYear(availableFiles.Keys);

            if (year == null)
            {
                result.ErrorMessage = "Could not find a GreenLuma DLL in the archive.";
                return result;
            }

            progressCallback?.Invoke("Deploying files...");
            Directory.CreateDirectory(destinationPath);

            var (stealthFiles, fullOnlyFiles) = GreenLumaService.GetRequiredFileNames(year, "64");

            DeployFlatFiles(stealthFiles, availableFiles, sourceDir, destinationPath, result.DeployedFiles);

            if (mode == GreenLumaLaunchMode.Normal)
            {
                DeployFlatFiles(
                    fullOnlyFiles.Where(f => !f.Contains(Path.DirectorySeparatorChar)),
                    availableFiles, sourceDir, destinationPath, result.DeployedFiles);
                DeployAssetsFolder(sourceDir, destinationPath, year, result.DeployedFiles);
                DeployLaunchers(availableFiles, sourceDir, destinationPath, result.DeployedFiles);
            }

            DeployOptionalFile("AppListManager.exe", availableFiles, sourceDir, destinationPath, result.DeployedFiles);

            result.Success = result.DeployedFiles.Count > 0;
            if (!result.Success)
                result.ErrorMessage = "No matching GreenLuma files were found in the archive.";

            progressCallback?.Invoke("Done.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaDeploymentService.Deploy");
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            TryCleanup(tempDir);
        }

        return result;
    }

    private static GreenLumaDeploymentResult DeployFullStealth(
        string sourceDir, string destinationPath, FullStealthVariant variant,
        GreenLumaDeploymentResult result, Action<string>? progressCallback)
    {
        var stealthDir = string.Equals(Path.GetFileName(sourceDir), "StealthMode", StringComparison.OrdinalIgnoreCase)
            ? sourceDir
            : Directory.GetDirectories(sourceDir, "StealthMode", SearchOption.AllDirectories).FirstOrDefault();
        if (stealthDir == null)
        {
            result.ErrorMessage = "The archive does not contain a StealthMode folder.";
            return result;
        }

        var sourceAppList = Path.Combine(stealthDir, "AppList");
        var standardDll = Path.Combine(stealthDir, "user32.dll");
        var familiesDll = Path.Combine(stealthDir, "user32SF.dll");
        if (!File.Exists(standardDll) || !File.Exists(familiesDll) || !Directory.Exists(sourceAppList))
        {
            result.ErrorMessage = "StealthMode is missing user32.dll, user32SF.dll, or AppList.";
            return result;
        }

        progressCallback?.Invoke("Installing Full Stealth source files...");
        Directory.CreateDirectory(destinationPath);
        CopyFile(standardDll, Path.Combine(destinationPath, "user32.dll"), "user32.dll", result.DeployedFiles);
        CopyFile(familiesDll, Path.Combine(destinationPath, "user32SF.dll"), "user32SF.dll", result.DeployedFiles);
        CopyDirectory(sourceAppList, Path.Combine(destinationPath, "AppList"));
        result.DeployedFiles.Add("AppList" + Path.DirectorySeparatorChar);

        var cacheTool = Path.Combine(stealthDir, "DeleteSteamAppCache.exe");
        if (File.Exists(cacheTool))
            CopyFile(cacheTool, Path.Combine(destinationPath, "DeleteSteamAppCache.exe"),
                "DeleteSteamAppCache.exe", result.DeployedFiles);

        result.Success = true;
        progressCallback?.Invoke("Done.");
        return result;
    }

    public static bool StageFullStealthForLaunch(Config config)
    {
        var sourceDllName = config.FullStealthVariant == FullStealthVariant.SteamFamilies
            ? "user32SF.dll"
            : "user32.dll";
        var sourceDll = Path.Combine(config.GreenLumaPath, sourceDllName);
        var sourceAppList = Path.Combine(config.GreenLumaPath, "AppList");
        if (!File.Exists(sourceDll) || !Directory.Exists(sourceAppList)) return false;

        RemoveFullStealthDeployment(config.SteamPath);

        var markerPath = Path.Combine(config.SteamPath, FullStealthMarkerName);
        var destinationDll = Path.Combine(config.SteamPath, "user32.dll");
        var dllBackup = Path.Combine(config.SteamPath, User32BackupName);
        var destinationAppList = Path.Combine(config.SteamPath, "AppList");
        var appListBackup = Path.Combine(config.SteamPath, AppListBackupName);

        try
        {
            if (File.Exists(destinationDll) && !File.Exists(dllBackup))
                File.Move(destinationDll, dllBackup);
            if (Directory.Exists(destinationAppList) && !Directory.Exists(appListBackup))
                Directory.Move(destinationAppList, appListBackup);

            File.WriteAllText(markerPath, config.FullStealthVariant.ToString());
            File.Copy(sourceDll, destinationDll, true);
            CopyDirectory(sourceAppList, destinationAppList);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaDeploymentService.StageFullStealth");
            RemoveFullStealthDeployment(config.SteamPath);
            return false;
        }
    }

    public static void RemoveFullStealthDeployment(string steamPath)
    {
        if (string.IsNullOrWhiteSpace(steamPath)) return;

        var markerPath = Path.Combine(steamPath, FullStealthMarkerName);
        if (!File.Exists(markerPath)) return;

        var destinationDll = Path.Combine(steamPath, "user32.dll");
        var backupPath = Path.Combine(steamPath, User32BackupName);
        var destinationAppList = Path.Combine(steamPath, "AppList");
        var appListBackup = Path.Combine(steamPath, AppListBackupName);
        if (File.Exists(backupPath))
        {
            File.Copy(backupPath, destinationDll, true);
            File.Delete(backupPath);
        }
        else if (File.Exists(destinationDll))
        {
            File.Delete(destinationDll);
        }

        if (Directory.Exists(destinationAppList))
            Directory.Delete(destinationAppList, true);
        if (Directory.Exists(appListBackup))
            Directory.Move(appListBackup, destinationAppList);

        File.Delete(markerPath);
    }

    private static bool TryExtractArchive(string archivePath, string extractDir, out string error)
    {
        error = string.Empty;

        try
        {
            var readerOptions = new ReaderOptions { Password = ArchivePassword };
            using var archive = ArchiveFactory.Open(archivePath, readerOptions);

            if (!AllEntriesStayWithinRoot(archive, extractDir))
            {
                error = "The archive contains an entry that would extract outside the destination folder.";
                return false;
            }

            var extractionOptions = new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            };

            archive.WriteToDirectory(extractDir, extractionOptions);

            if (Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories).Length == 0)
            {
                error = "The archive appears to be empty.";
                return false;
            }

            return true;
        }
        catch (CryptographicException ex)
        {
            Logger.Error(ex, "GreenLumaDeploymentService.Extract");
            error = "Failed to decrypt the archive. It may use a different password.";
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaDeploymentService.Extract");
            error = $"Failed to extract the archive: {ex.Message}";
            return false;
        }
    }

    private static bool AllEntriesStayWithinRoot(IArchive archive, string extractDir)
    {
        var root = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
                continue;

            var resolvedPath = Path.GetFullPath(Path.Combine(extractDir, entry.Key));
            if (!resolvedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string FindSourceDirectory(string extractDir)
    {
        var dir = new DirectoryInfo(extractDir);

        while (true)
        {
            var subDirs = dir.GetDirectories();
            var files = dir.GetFiles();

            if (subDirs.Length == 1 && files.Length == 0)
            {
                dir = subDirs[0];
                continue;
            }

            return dir.FullName;
        }
    }

    private static Dictionary<string, string> CollectAvailableFiles(string sourceDir)
    {
        const string preferredFolder = "NormalMode";
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(filePath);
            var relative = Path.GetRelativePath(sourceDir, filePath);

            if (!files.TryGetValue(fileName, out var existing))
            {
                files[fileName] = filePath;
                continue;
            }

            var existingRelative = Path.GetRelativePath(sourceDir, existing);
            if (relative.StartsWith(preferredFolder, StringComparison.OrdinalIgnoreCase) &&
                !existingRelative.StartsWith(preferredFolder, StringComparison.OrdinalIgnoreCase))
                files[fileName] = filePath;
        }

        return files;
    }

    private static string? DetectYear(IEnumerable<string> fileNames)
    {
        foreach (var name in fileNames)
        {
            var match = YearRegex().Match(name);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null;
    }

    private static void DeployFlatFiles(
        IEnumerable<string> targets,
        Dictionary<string, string> availableFiles,
        string sourceDir,
        string destinationPath,
        List<string> deployed)
    {
        foreach (var target in targets)
        {
            var sourceFile = ResolveSourceFile(target, availableFiles, sourceDir);
            if (sourceFile == null)
                continue;

            CopyFile(sourceFile, Path.Combine(destinationPath, target), target, deployed);
        }
    }

    private static void DeployOptionalFile(
        string target,
        Dictionary<string, string> availableFiles,
        string sourceDir,
        string destinationPath,
        List<string> deployed)
    {
        var sourceFile = ResolveSourceFile(target, availableFiles, sourceDir);
        if (sourceFile == null)
            return;

        CopyFile(sourceFile, Path.Combine(destinationPath, target), target, deployed);
    }

    private static void DeployAssetsFolder(string sourceDir, string destinationPath, string year, List<string> deployed)
    {
        var folderName = $"GreenLuma{year}_Files";
        var sourceFolder = Path.Combine(sourceDir, folderName);

        if (!Directory.Exists(sourceFolder))
            sourceFolder = Directory.GetDirectories(sourceDir, folderName, SearchOption.AllDirectories)
                .FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
            return;

        var destFolder = Path.Combine(destinationPath, folderName);
        CopyDirectory(sourceFolder, destFolder);
        deployed.Add(folderName + Path.DirectorySeparatorChar);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);

        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    private static void DeployLaunchers(
        Dictionary<string, string> availableFiles, string sourceDir, string destinationPath, List<string> deployed)
    {
        foreach (var launcherName in new[] { "x86launcher.exe", "x64launcher.exe" })
        {
            var launcherSource = ResolveSourceFile(launcherName, availableFiles, sourceDir);
            if (launcherSource == null)
                continue;

            var binDir = Path.Combine(destinationPath, "bin");
            Directory.CreateDirectory(binDir);
            var launcherDest = Path.Combine(binDir, launcherName);

            if (File.Exists(launcherDest))
            {
                var backupPath = launcherDest + ".og";
                try
                {
                    if (!File.Exists(backupPath))
                        File.Move(launcherDest, backupPath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"GreenLumaDeploymentService.BackupLauncher: {launcherName}");
                }
            }

            CopyFile(launcherSource, launcherDest, Path.Combine("bin", launcherName), deployed);
        }
    }

    private static string? ResolveSourceFile(string target, Dictionary<string, string> availableFiles, string sourceDir)
    {
        var fileName = Path.GetFileName(target);

        if (availableFiles.TryGetValue(fileName, out var path))
            return path;

        var directPath = Path.Combine(sourceDir, target);
        return File.Exists(directPath) ? directPath : null;
    }

    private static void CopyFile(string sourcePath, string destPath, string label, List<string> deployed)
    {
        try
        {
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(sourcePath, destPath, true);
            deployed.Add(label);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"GreenLumaDeploymentService.CopyFile: {label}");
        }
    }

    private static void TryCleanup(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaDeploymentService.Cleanup");
        }
    }
}
