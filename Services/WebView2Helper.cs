using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace GreenLuma_Manager.Services;

public static class WebView2Helper
{
    private const string LoaderResourceName = "GreenLuma_Manager.Native.WebView2Loader.dll";

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GLM_Manager");

    private static readonly string UserDataDir = Path.Combine(AppDataDir, "WebView2");
    private static readonly string LoaderPath = Path.Combine(AppDataDir, "WebView2Loader.dll");

    private static CoreWebView2Environment? _cachedEnvironment;
    private static bool _loaderReady;

    public static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_cachedEnvironment is not null)
            return _cachedEnvironment;

        EnsureLoaderExtracted();

        if (!Directory.Exists(UserDataDir))
            Directory.CreateDirectory(UserDataDir);

        _cachedEnvironment = await CoreWebView2Environment.CreateAsync(
            null,
            UserDataDir).ConfigureAwait(false);

        return _cachedEnvironment;
    }

    private static void EnsureLoaderExtracted()
    {
        if (_loaderReady) return;

        if (!Directory.Exists(AppDataDir))
            Directory.CreateDirectory(AppDataDir);

        try
        {
            using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LoaderResourceName)
                                       ?? throw new InvalidOperationException(
                                           $"Embedded resource '{LoaderResourceName}' not found.");
            using var fileStream = new FileStream(LoaderPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            resourceStream.CopyTo(fileStream);
        }
        catch (IOException) when (File.Exists(LoaderPath))
        {
        }

        NativeLibrary.Load(LoaderPath);
        _loaderReady = true;
    }
}