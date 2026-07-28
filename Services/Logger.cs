using System.IO;
using System.Runtime.CompilerServices;

namespace GreenLuma_Manager.Services;

public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GLM_Manager");

    private static readonly string LogPath = Path.Combine(LogDir, "GreenLuma-Manager.log");
    private static readonly string PrevLogPath = Path.Combine(LogDir, "GreenLuma-Manager.prev.log");
    private static readonly object Lock = new();

    static Logger()
    {
        try
        {
            if (!Directory.Exists(LogDir))
                Directory.CreateDirectory(LogDir);

            if (File.Exists(LogPath))
                File.Move(LogPath, PrevLogPath, true);
        }
        catch
        {
        }
    }

    public static void Info(
        string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        Write("Info", message, memberName, filePath, lineNumber);
    }

    public static void Debug(
        string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        Write("Debug", message, memberName, filePath, lineNumber);
    }

    public static void Warn(
        string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        Write("Warn", message, memberName, filePath, lineNumber);
    }

    public static void Error(
        string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        Write("Error", message, memberName, filePath, lineNumber);
    }

    public static void Error(
        Exception ex,
        string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        Write("Error", $"{message}: {ex.GetType().Name}: {ex.Message}", memberName, filePath, lineNumber);
    }

    private static void Write(string level, string message, string memberName, string filePath, int lineNumber)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var entry = $"[{timestamp}] [{level}] [{fileName}:{memberName}:{lineNumber}] {message}";

            lock (Lock)
            {
                File.AppendAllText(LogPath, entry + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}