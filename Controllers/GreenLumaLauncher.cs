using System.IO;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;

namespace GreenLuma_Manager.Controllers;

public class GreenLumaLauncher
{
    public bool ValidatePaths(Config? config)
    {
        return config != null
               && !string.IsNullOrWhiteSpace(config.GreenLumaPath)
               && Directory.Exists(config.GreenLumaPath);
    }

    public bool IsAppListGenerated(Config config)
    {
        var appListPath = Path.Combine(config.GreenLumaPath, "AppList");
        return Directory.Exists(appListPath) && Directory.GetFiles(appListPath, "*.txt").Length > 0;
    }

    public Task<bool> LaunchAsync(Config config)
    {
        return GreenLumaService.LaunchGreenLumaAsync(config);
    }
}