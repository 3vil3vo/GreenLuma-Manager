using System.Diagnostics;
using System.IO;

namespace GreenLuma_Manager.Services;

public static class LogService
{
    private const long MaxLogSize = 1024 * 1024;

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GLM_Manager");

    private static readonly string LogPath = Path.Combine(LogDir, "app.log");
    private static readonly object Lock = new();

    public static void LogError(string context, Exception ex)
    {
        try
        {
            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);

            lock (Lock)
            {
                RotateIfNeeded();
                var entry =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR [{context}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}";
                File.AppendAllText(LogPath, entry);
            }

            Debug.WriteLine($"[{context}] {ex.GetType().Name}: {ex.Message}");
        }
        catch
        {
            // ignored
        }
    }

    public static void LogWarning(string context, string message)
    {
        try
        {
            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);

            lock (Lock)
            {
                RotateIfNeeded();
                var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WARN  [{context}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, entry);
            }

            Debug.WriteLine($"[{context}] WARN: {message}");
        }
        catch
        {
            // ignored
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath)) return;
        if (new FileInfo(LogPath).Length <= MaxLogSize) return;

        var backup = LogPath + ".old";
        if (File.Exists(backup)) File.Delete(backup);
        File.Move(LogPath, backup);
    }
}