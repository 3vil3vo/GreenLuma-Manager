using System.Collections.Concurrent;
using System.IO;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;

namespace GreenLuma_Manager.Controllers;

public class AppListController
{
    private readonly GameListController _gameListController;
    private readonly GreenLumaLauncher _launcher;
    private readonly NotificationManager _notificationManager;
    private readonly ProfileController _profileController;

    public AppListController(
        ProfileController profileController,
        GameListController gameListController,
        GreenLumaLauncher launcher,
        NotificationManager notificationManager)
    {
        _profileController = profileController;
        _gameListController = gameListController;
        _launcher = launcher;
        _notificationManager = notificationManager;
    }

    public async Task<ImportResult> ImportExistingAppListAsync(Config config)
    {
        var result = new ImportResult();

        var steamAppListPath = !string.IsNullOrWhiteSpace(config.SteamPath)
            ? Path.Combine(config.SteamPath, "AppList")
            : null;
        var greenLumaAppListPath = !string.IsNullOrWhiteSpace(config.GreenLumaPath)
            ? Path.Combine(config.GreenLumaPath, "AppList")
            : null;

        var steamHasAppList = steamAppListPath != null && GreenLumaService.IsAppListGenerated(steamAppListPath);
        var greenLumaHasAppList =
            greenLumaAppListPath != null && GreenLumaService.IsAppListGenerated(greenLumaAppListPath);

        if (!steamHasAppList && !greenLumaHasAppList)
        {
            result.FoundAppList = false;
            return result;
        }

        result.FoundAppList = true;
        result.FoundInSteamFolder = steamHasAppList;
        result.HasSteamWarning = steamHasAppList;

        var appListPath = steamHasAppList ? steamAppListPath! : greenLumaAppListPath!;
        result.AppIds = await ReadAppIdsAsync(appListPath).ConfigureAwait(false);

        return result;
    }

    private static async Task<List<string>> ReadAppIdsAsync(string appListPath)
    {
        var appIds = new HashSet<string>();

        var txtFiles = Directory.GetFiles(appListPath, "*.txt");
        if (txtFiles.Length > 0)
        {
            foreach (var file in txtFiles)
            {
                var id = (await File.ReadAllTextAsync(file).ConfigureAwait(false)).Trim();
                if (!string.IsNullOrWhiteSpace(id)) appIds.Add(id);
            }

            return [.. appIds];
        }

        var iniPath = Path.Combine(appListPath, "AppList.ini");
        if (File.Exists(iniPath))
            foreach (var id in GreenLumaService.ReadAppIdsFromIni(iniPath))
                appIds.Add(id);

        return [.. appIds];
    }

    public async Task ResolveAndImportAppsAsync(
        List<string> appIds,
        Profile profile,
        IProgress<AppListProgressReport>? progress = null)
    {
        var allFoundDepotIds = new HashSet<string>();
        var packageInfos = new ConcurrentDictionary<string, AppPackageInfo?>();
        var semaphore = new SemaphoreSlim(6);
        var tasks = new List<Task>();
        var resolvedCount = 0;

        progress?.Report(new AppListProgressReport
        {
            Status = $"Resolving package info... 0/{appIds.Count}",
            Current = 0, Total = appIds.Count, IsIndeterminate = false
        });

        foreach (var id in appIds)
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var info = await DepotService.FetchAppPackageInfoAsync(id).ConfigureAwait(false);
                    if (info != null)
                    {
                        packageInfos[id] = info;
                        lock (allFoundDepotIds)
                        {
                            foreach (var depotId in info.Depots) allFoundDepotIds.Add(depotId);
                            foreach (var depotList in info.DlcDepots.Values)
                            foreach (var depotId in depotList)
                                allFoundDepotIds.Add(depotId);
                        }
                    }

                    var count = Interlocked.Increment(ref resolvedCount);
                    progress?.Report(new AppListProgressReport
                    {
                        Status = $"Resolving package info... {count}/{appIds.Count}",
                        Current = count, Total = appIds.Count, IsIndeterminate = false
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        tasks.Clear();

        var mainAppIds = appIds.Where(id => !allFoundDepotIds.Contains(id)).ToList();
        var importedGames = new ConcurrentBag<Game>();
        var importedCount = 0;

        progress?.Report(new AppListProgressReport
        {
            Status = $"Importing games... 0/{mainAppIds.Count}",
            Current = 0, Total = mainAppIds.Count, IsIndeterminate = false
        });

        foreach (var id in mainAppIds)
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (!packageInfos.TryGetValue(id, out var info) || info == null) return;

                    var game = new Game { AppId = id, Name = string.Empty, Type = "Game" };
                    await SearchService.PopulateGameDetailsAsync(game).ConfigureAwait(false);

                    List<string>? depotsToAssign = null;
                    var parentInfo = packageInfos.Values
                        .Where(p => p != null)
                        .FirstOrDefault(p => p!.DlcAppIds.Contains(id));

                    if (parentInfo != null)
                    {
                        if (parentInfo.DlcDepots.TryGetValue(id, out var dlcDepots))
                            depotsToAssign = dlcDepots;
                    }
                    else if (info.Depots.Count > 0)
                    {
                        depotsToAssign = info.Depots;
                    }
                    else if (info.DlcDepots.TryGetValue(id, out var dlcDepots))
                    {
                        depotsToAssign = dlcDepots;
                    }

                    if (depotsToAssign != null)
                        game.Depots = depotsToAssign.Where(depotId => appIds.Contains(depotId)).ToList();

                    if (!string.IsNullOrWhiteSpace(game.IconUrl))
                    {
                        var path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId, game.IconUrl)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(path)) game.IconUrl = path;
                    }

                    importedGames.Add(game);

                    var count = Interlocked.Increment(ref importedCount);
                    progress?.Report(new AppListProgressReport
                    {
                        Status = $"Importing games... {count}/{mainAppIds.Count}",
                        Current = count, Total = mainAppIds.Count, IsIndeterminate = false
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "AppListController.ResolveImport");
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var allGames = _gameListController.Games.Concat(importedGames).ToList();

        foreach (var depotId in appIds.Where(id => allFoundDepotIds.Contains(id)))
        {
            string? parentAppId = null;
            foreach (var info in packageInfos.Values.Where(i => i != null))
            {
                if (info!.Depots.Contains(depotId))
                {
                    parentAppId = info.AppId;
                    break;
                }

                foreach (var pair in info.DlcDepots)
                    if (pair.Value.Contains(depotId))
                    {
                        parentAppId = pair.Key;
                        break;
                    }

                if (parentAppId != null) break;
            }

            if (parentAppId == null) continue;

            var parentGame = allGames.FirstOrDefault(g => g.AppId == parentAppId);
            if (parentGame != null && !parentGame.Depots.Contains(depotId))
                parentGame.Depots.Add(depotId);
        }

        foreach (var game in importedGames)
            profile.Games.Add(game);

        ProfileService.Save(profile);

        if (string.Equals(_profileController.CurrentProfile?.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
            _gameListController.LoadGames(profile.Games);

        var totalDepotsIncluded = importedGames.Sum(g => g.Depots.Count);
        _notificationManager.ShowToast(
            $"Added {importedGames.Count} Games/DLCs & {totalDepotsIncluded} Depots from {appIds.Count} IDs");

        progress?.Report(new AppListProgressReport
        {
            Status = "Done",
            Current = appIds.Count, Total = appIds.Count, IsIndeterminate = false
        });
    }

    public async Task<int> GenerateAsync(Config? config, Profile? profile)
    {
        if (config == null || profile == null) return -1;
        return await GreenLumaService.GenerateAppListAsync(profile, config).ConfigureAwait(false);
    }

    public bool ValidatePathsForGeneration(Config? config)
    {
        return config != null &&
               !string.IsNullOrWhiteSpace(config.GreenLumaPath) &&
               _launcher.ValidatePaths(config);
    }
}

public class ImportResult
{
    public bool FoundAppList { get; set; }
    public bool FoundInSteamFolder { get; set; }
    public bool HasSteamWarning { get; set; }
    public List<string> AppIds { get; set; } = [];
}