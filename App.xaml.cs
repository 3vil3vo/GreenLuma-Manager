using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace GreenLuma_Manager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                // Load localization before any windows show
                try
                {
                    var cfg = ConfigService.Load();
                    if (string.IsNullOrWhiteSpace(cfg.Language))
                    {
                        cfg.Language = LocalizationService.GetDefaultLanguage();
                        ConfigService.Save(cfg);
                    }
                    LocalizationService.ApplyLanguage(cfg.Language);
                }
                catch
                {
                }

                if (e.Args != null && e.Args.Length > 0)
                {
                    foreach (var arg in e.Args)
                    {
                        if (string.Equals(arg, "--launch-greenluma", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var config = ConfigService.Load();
                                GreenLumaService.LaunchGreenLumaAsync(config).GetAwaiter().GetResult();
                            }
                            catch
                            {
                            }

                            Shutdown();
                            return;
                        }
                    }
                }

                var profiles = ProfileService.LoadAll();
                var valid = new HashSet<string>(profiles
                    .SelectMany(p => p.Games)
                    .Where(g => !string.IsNullOrWhiteSpace(g.AppId))
                    .Select(g => g.AppId));
                IconCacheService.DeleteUnusedIcons(valid);
                _ = Task.Run(() => WarmupIconsAsync(profiles));
            }
            catch
            {
            }
        }

        private static async Task WarmupIconsAsync(List<Profile> profiles)
        {
            try
            {
                var semaphore = new SemaphoreSlim(6);
                var tasks = new List<Task>();
                foreach (var profile in profiles)
                {
                    bool changed = false;
                    foreach (var game in profile.Games)
                    {
                        if (string.IsNullOrWhiteSpace(game.AppId))
                            continue;
                        string? cached = IconCacheService.GetCachedIconPath(game.AppId);
                        if (string.IsNullOrEmpty(cached))
                        {
                            await semaphore.WaitAsync();
                            var t = Task.Run(async () =>
                            {
                                try
                                {
                                    string? path = null;
                                    if (!string.IsNullOrWhiteSpace(game.IconUrl))
                                    {
                                        path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId,
                                            game.IconUrl);
                                    }

                                    if (string.IsNullOrEmpty(path))
                                    {
                                        await SearchService.FetchIconUrlAsync(game);
                                        if (!string.IsNullOrWhiteSpace(game.IconUrl))
                                        {
                                            path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId,
                                                game.IconUrl);
                                        }
                                    }

                                    if (!string.IsNullOrEmpty(path))
                                    {
                                        game.IconUrl = path;
                                        changed = true;
                                    }
                                }
                                catch
                                {
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
                                    string? path =
                                        await IconCacheService.DownloadAndCacheIconAsync(game.AppId, game.IconUrl);
                                    if (!string.IsNullOrEmpty(path))
                                    {
                                        game.IconUrl = path;
                                        changed = true;
                                    }
                                }
                                catch
                                {
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
                    if (changed)
                    {
                        try
                        {
                            ProfileService.Save(profile);
                        }
                        catch
                        {
                        }
                    }

                    tasks.Clear();
                }
            }
            catch
            {
            }
        }
    }
}
