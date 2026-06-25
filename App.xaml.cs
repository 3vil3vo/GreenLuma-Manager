using System.IO;
using System.Windows;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;

namespace GreenLuma_Manager;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            PluginService.Initialize();
            PluginService.OnApplicationStartup();

            var config = ConfigService.Load();

            if (e.Args.Length > 0)
                foreach (var arg in e.Args)
                    if (string.Equals(arg, "--launch-greenluma", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            GreenLumaService.LaunchGreenLumaAsync(config).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            LogService.LogError("App.LaunchGreenluma", ex);
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
            _ = SearchService.PrefetchAsync(config);
            _ = Task.Run(() => { _ = SteamService.Instance; });
        }
        catch (Exception ex)
        {
            LogService.LogError("App.OnStartup", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            SteamService.Instance.Dispose();
            PluginService.OnApplicationShutdown();
        }
        catch (Exception ex)
        {
            LogService.LogError("App.OnExit", ex);
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
                            LogService.LogError("App.WarmupIcon", ex);
                        }
                    });

                if (changed)
                    try
                    {
                        ProfileService.Save(profile);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("App.WarmupSave", ex);
                    }
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("App.WarmupIcons", ex);
        }
    }
}