using System.IO;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;

namespace GreenLuma_Manager.Controllers;

public class GreenLumaLauncher
{
    public bool ValidatePaths(Config? config)
    {
        return config != null
               && !string.IsNullOrWhiteSpace(GreenLumaService.GetRuntimeRootPath(config))
               && Directory.Exists(GreenLumaService.GetRuntimeRootPath(config));
    }

    public bool IsAppListGenerated(Config config)
    {
        return GreenLumaService.IsAppListGenerated(config);
    }

    public Task<bool> LaunchAsync(Config config)
    {
        return GreenLumaService.LaunchGreenLumaAsync(config);
    }
}
