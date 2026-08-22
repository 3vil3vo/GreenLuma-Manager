using System.IO;
using System.Windows;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;

namespace GreenLuma_Manager;

public partial class App
{
    private Config? _config;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            PluginService.Initialize();
            PluginService.OnApplicationStartup();

            var config = ConfigService.Load();
            _config = config;

            if (e.Args.Length > 0)
                foreach (var arg in e.Args)
                    if (string.Equals(arg, "--launch-greenluma", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            GreenLumaVersionPromptService.EnsureConfirmed(config);
                            var launched = GreenLumaService.LaunchGreenLumaAsync(config).GetAwaiter().GetResult();
                            if (launched && config.LaunchMode == GreenLumaLaunchMode.FullStealth)
                                GreenLumaService.MonitorSteamAfterLaunchAsync(config).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "App.LaunchGreenluma");
                        }

                        Shutdown();
                        return;
                    }

            var profiles = ProfileService.LoadAll();
            var valid = new HashSet<string>(profiles
                .SelectMany(p => p.Games)
                .Where(g => !string.IsNullOrWhiteSpace(g.AppId))
                .Select(g => g.AppId));
            IconCacheService.DeleteUnusedIcons(valid);
            _ = WarmupIconsAsync(profiles);
            SearchService.SetApiKey(config.SteamApiKey);
            _ = SearchService.PrefetchAsync(config);
            _ = Task.Run(() => { _ = SteamService.Instance; });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "App.OnStartup");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_config?.LaunchMode == GreenLumaLaunchMode.FullStealth)
                GreenLumaDeploymentService.RemoveFullStealthDeployment(_config.SteamPath);
            SteamService.Instance.Dispose();
            PluginService.OnApplicationShutdown();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "App.OnExit");
        }

        base.OnExit(e);
    }

    private static async Task WarmupIconsAsync(List<Profile> profiles)
    {
        try
        {
            foreach (var profile in profiles)
            {
                var changed = false;

                await Parallel.ForEachAsync(
                    profile.Games.Where(g => !string.IsNullOrWhiteSpace(g.AppId)),
                    new ParallelOptions { MaxDegreeOfParallelism = 6 },
                    async (game, _) =>
                    {
                        try
                        {
                            var cached = IconCacheService.GetCachedIconPath(game.AppId);
                            if (string.IsNullOrEmpty(cached))
                            {
                                string? path = null;
                                if (!string.IsNullOrWhiteSpace(game.IconUrl))
                                    path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId, game.IconUrl);

                                if (string.IsNullOrEmpty(path))
                                {
                                    await SearchService.FetchIconUrlAsync(game);
                                    if (!string.IsNullOrWhiteSpace(game.IconUrl))
                                        path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId,
                                            game.IconUrl);
                                }

                                if (!string.IsNullOrEmpty(path))
                                {
                                    game.IconUrl = path;
                                    changed = true;
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(game.IconUrl) && !File.Exists(cached))
                            {
                                var path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId, game.IconUrl);
                                if (!string.IsNullOrEmpty(path))
                                {
                                    game.IconUrl = path;
                                    changed = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "App.WarmupIcon");
                        }
                    });

                if (changed)
                    try
                    {
                        ProfileService.Save(profile);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "App.WarmupSave");
                    }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "App.WarmupIcons");
        }
    }
}
