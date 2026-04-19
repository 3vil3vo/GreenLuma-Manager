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
                        return;
                    }

            var profiles = ProfileService.LoadAll();
            var valid = new HashSet<string>(profiles
                .SelectMany(p => p.Games)
                .Where(g => !string.IsNullOrWhiteSpace(g.AppId))
                .Select(g => g.AppId));
            IconCacheService.DeleteUnusedIcons(valid);
            _ = Task.Run(() => WarmupIconsAsync(profiles));
            _ = SearchService.PrefetchAsync(config);
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
            using var semaphore = new SemaphoreSlim(6);
            var tasks = new List<Task>();
            foreach (var profile in profiles)
            {
                var changed = 0;
                foreach (var game in profile.Games)
                {
                    if (string.IsNullOrWhiteSpace(game.AppId))
                        continue;
                    var cached = IconCacheService.GetCachedIconPath(game.AppId);
                    if (string.IsNullOrEmpty(cached))
                    {
                        await semaphore.WaitAsync();
                        var t = Task.Run(async () =>
                        {
                            try
                            {
                                string? path = null;
                                if (!string.IsNullOrWhiteSpace(game.IconUrl))
                                    path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId,
                                        game.IconUrl);

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
                                    Interlocked.Exchange(ref changed, 1);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.LogError("App.WarmupIcon", ex);
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });
                        tasks.Add(t);
                    }
                    else if (!string.IsNullOrWhiteSpace(game.IconUrl) && !File.Exists(cached))
                    {
                        await semaphore.WaitAsync();
                        var t2 = Task.Run(async () =>
                        {
                            try
                            {
                                var path =
                                    await IconCacheService.DownloadAndCacheIconAsync(game.AppId, game.IconUrl);
                                if (!string.IsNullOrEmpty(path))
                                {
                                    game.IconUrl = path;
                                    Interlocked.Exchange(ref changed, 1);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.LogError("App.WarmupIconRedownload", ex);
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });
                        tasks.Add(t2);
                    }
                }

                await Task.WhenAll(tasks);
                if (changed != 0)
                    try
                    {
                        ProfileService.Save(profile);
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("App.WarmupSave", ex);
                    }

                tasks.Clear();
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("App.WarmupIcons", ex);
        }
    }
}