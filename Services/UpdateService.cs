using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using GreenLuma_Manager.Models;

namespace GreenLuma_Manager.Services;

public partial class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/3vil3vo/GreenLuma-Manager/releases/latest";
    private const string AllowedDownloadHost = "github.com";
    private const string AllowedDownloadPathPrefix = "/3vil3vo/GreenLuma-Manager/";
    private static readonly string[] RcSeparator = ["-rc"];

    private static string CurrentVersion => MainWindow.Version;

    [GeneratedRegex("\"tag_name\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex TagNameRegex();

    [GeneratedRegex("\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex BrowserDownloadUrlRegex();

    [GeneratedRegex("\"body\"\\s*:\\s*\"([^\"]*)\"")]
    private static partial Regex BodyRegex();

    public static async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var response = await HttpClientProvider.GitHub.GetStringAsync(GitHubApiUrl).ConfigureAwait(false);
            return ParseUpdateInfo(response);
        }
        catch (Exception ex)
        {
            LogService.LogError("UpdateService.CheckForUpdates", ex);
            return null;
        }
    }

    private static UpdateInfo? ParseUpdateInfo(string jsonResponse)
    {
        var tagMatch = TagNameRegex().Match(jsonResponse);
        var downloadMatch = BrowserDownloadUrlRegex().Match(jsonResponse);
        var bodyMatch = BodyRegex().Match(jsonResponse);

        if (!tagMatch.Success)
            return null;

        var latestTag = tagMatch.Groups[1].Value;
        var downloadUrl = downloadMatch.Success ? downloadMatch.Groups[1].Value : string.Empty;
        var releaseNotes = bodyMatch.Success
            ? bodyMatch.Groups[1].Value.Replace("\\n", "\n").Replace("\\r", "\r")
            : string.Empty;

        var currentNormalized = NormalizeVersion(CurrentVersion);
        var latestNormalized = NormalizeVersion(latestTag);

        return new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = FormatDisplayVersion(latestTag),
            LatestVersionTag = latestTag,
            UpdateAvailable =
                string.Compare(latestNormalized, currentNormalized, StringComparison.OrdinalIgnoreCase) > 0,
            DownloadUrl = downloadUrl,
            ReleaseNotes = releaseNotes
        };
    }

    private static string NormalizeVersion(string version)
    {
        var cleaned = version.ToLower().Trim().Replace("v", "");

        if (string.IsNullOrEmpty(cleaned))
            return "00000.00000.00000.0000.0000";

        if (cleaned.StartsWith("rc") && !cleaned.Contains(".0.0"))
        {
            var rcPart = cleaned[2..];
            var rcParts = rcPart.Split('.');

            var rcMajor = ParseVersionPart(rcParts, 0);
            var rcMinor = rcParts.Length > 1 ? ParseVersionPart(rcParts, 1) : 0;

            return $"00001.00000.00000.{rcMajor:D4}.{rcMinor:D4}";
        }

        var parts = cleaned.Split('-', 2);
        var baseVersion = parts[0];

        var versionParts = baseVersion.Split('.');
        var major = ParseVersionPart(versionParts, 0);
        var minor = ParseVersionPart(versionParts, 1);
        var patch = ParseVersionPart(versionParts, 2);

        if (parts.Length == 1) return $"{major:D5}.{minor:D5}.{patch:D5}.9999.9999";

        var prerelease = parts[1];

        if (prerelease.StartsWith("rc"))
        {
            var rcPart = prerelease[2..];
            var rcParts = rcPart.Split('.');

            var rcMajor = ParseVersionPart(rcParts, 0);
            var rcMinor = rcParts.Length > 1 ? ParseVersionPart(rcParts, 1) : 0;

            return $"{major:D5}.{minor:D5}.{patch:D5}.{rcMajor:D4}.{rcMinor:D4}";
        }

        return $"{major:D5}.{minor:D5}.{patch:D5}.0000.0000";
    }

    private static int ParseVersionPart(string[] parts, int index)
    {
        if (index >= parts.Length)
            return 0;

        if (int.TryParse(parts[index], out var result))
            return result;

        return 0;
    }

    private static string FormatDisplayVersion(string version)
    {
        var cleaned = version.ToLower().Replace("v", "");

        if (cleaned.Contains("-rc"))
        {
            var parts = cleaned.Split(RcSeparator, StringSplitOptions.None);
            if (parts.Length > 1) return "RC" + parts[1].ToUpper();
        }

        if (cleaned.StartsWith("rc")) return cleaned.ToUpper();

        return cleaned;
    }

    public static async Task<bool> PerformAutoUpdateAsync(string downloadUrl)
    {
        try
        {
            if (!IsValidDownloadUrl(downloadUrl))
            {
                LogService.LogError("UpdateService.PerformAutoUpdate",
                    new InvalidOperationException($"Blocked untrusted download URL: {downloadUrl}"));
                return false;
            }

            var currentExePath = Environment.ProcessPath!;
            var tempExePath = await DownloadUpdate(downloadUrl).ConfigureAwait(false);

            if (!IsValidPeFile(tempExePath))
            {
                File.Delete(tempExePath);
                LogService.LogError("UpdateService.PerformAutoUpdate",
                    new InvalidOperationException("Downloaded file is not a valid PE executable"));
                return false;
            }

            CreateAndExecuteUpdateScript(tempExePath, currentExePath);

            return true;
        }
        catch (Exception ex)
        {
            LogService.LogError("UpdateService.PerformAutoUpdate", ex);
            return false;
        }
    }

    private static bool IsValidDownloadUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               uri.Host.EndsWith(AllowedDownloadHost, StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.StartsWith(AllowedDownloadPathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidPeFile(string filePath)
    {
        try
        {
            var header = new byte[2];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return fs.Read(header, 0, 2) == 2 && header[0] == 0x4D && header[1] == 0x5A;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> DownloadUpdate(string downloadUrl)
    {
        var tempDir = Path.GetTempPath();
        var tempExePath = Path.Combine(tempDir, "GreenLumaManager_Update.exe");

        using var response = await HttpClientProvider.GitHub.GetAsync(downloadUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var fileStream = new FileStream(tempExePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream).ConfigureAwait(false);

        return tempExePath;
    }

    private static void CreateAndExecuteUpdateScript(string tempExePath, string currentExePath)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "update.bat");
        var scriptContent = GenerateUpdateScript(tempExePath, currentExePath);

        File.WriteAllText(scriptPath, scriptContent);

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string GenerateUpdateScript(string tempExePath, string currentExePath)
    {
        var currentExeName = Path.GetFileName(currentExePath);

        return $@"@echo off
echo Waiting for application to close...
timeout /t 2 /nobreak >nul

taskkill /F /IM ""{currentExeName}"" >nul 2>&1

echo Waiting for process to fully terminate...
:wait_process
tasklist /FI ""IMAGENAME eq {currentExeName}"" 2>NUL | find /I /N ""{currentExeName}"">NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >nul
    goto wait_process
)

echo Process terminated.
echo Updating...

del ""{currentExePath}"" >nul 2>&1
:wait_delete
if exist ""{currentExePath}"" (
    timeout /t 1 /nobreak >nul
    del ""{currentExePath}"" >nul 2>&1
    goto wait_delete
)

move /y ""{tempExePath}"" ""{currentExePath}""
if errorlevel 1 (
    echo Update failed!
    pause
    exit /b 1
)

echo.
echo ========================================
echo Update Complete!
echo ========================================
echo.
echo Restarting GreenLuma Manager...
timeout /t 2 /nobreak >nul

start """" ""{currentExePath}""
exit
";
    }
}