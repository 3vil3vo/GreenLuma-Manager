using GreenLuma_Manager.Dialogs;
using GreenLuma_Manager.Models;

namespace GreenLuma_Manager.Services;

public static class GreenLumaVersionPromptService
{
    public static void EnsureConfirmed(Config? config)
    {
        if (config == null || config.GreenLumaVersionPromptShown)
            return;

        var greenLumaPath = config.GreenLumaPath.Trim();
        if (string.IsNullOrWhiteSpace(greenLumaPath))
            return;

        var detected = GreenLumaService.DetectVersion(greenLumaPath);
        if (!string.Equals(detected, GreenLumaService.LegacyVersion, StringComparison.Ordinal))
            return;

        var chosen = GreenLumaVersionDialog.Show(detected);

        config.GreenLumaVersionOverride = chosen;
        config.GreenLumaVersionPromptShown = true;
        ConfigService.Save(config);
    }
}
