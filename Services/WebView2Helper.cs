using System.IO;
using Microsoft.Web.WebView2.Core;

namespace GreenLuma_Manager.Services;

public static class WebView2Helper
{
    private static readonly string UserDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GLM_Manager",
        "WebView2");

    private static CoreWebView2Environment? _cachedEnvironment;

    public static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_cachedEnvironment is not null)
            return _cachedEnvironment;

        if (!Directory.Exists(UserDataDir))
            Directory.CreateDirectory(UserDataDir);

        _cachedEnvironment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: UserDataDir,
            options: null).ConfigureAwait(false);

        return _cachedEnvironment;
    }
}
