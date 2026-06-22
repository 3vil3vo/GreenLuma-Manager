using System.Collections.Concurrent;
using System.IO;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;

namespace GreenLuma_Manager.Controllers;

public class AppListController
{
    private readonly ProfileController _profileController;
    private readonly GameListController _gameListController;
    private readonly GreenLumaLauncher _launcher;
    private readonly NotificationManager _notificationManager;

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

        var steamHasAppList = steamAppListPath != null && Directory.Exists(steamAppListPath) &&
                              Directory.GetFiles(steamAppListPath, "*.txt").Length > 0;
        var greenLumaHasAppList = greenLumaAppListPath != null && Directory.Exists(greenLumaAppListPath) &&
                                  Directory.GetFiles(greenLumaAppListPath, "*.txt").Length > 0;

        if (!steamHasAppList && !greenLumaHasAppList)
        {
            result.FoundAppList = false;
            return result;
        }

        result.FoundAppList = true;
        result.FoundInSteamFolder = steamHasAppList;

        var appListToImport = steamHasAppList ? steamAppListPath! : greenLumaAppListPath!;

        var appIds = new HashSet<string>();
        try
        {
            foreach (var file in Directory.GetFiles(appListToImport, "*.txt"))
            {
                var appId = (await File.ReadAllTextAsync(file)).Trim();
                if (!string.IsNullOrWhiteSpace(appId))
                    appIds.Add(appId);
            }
        }
        catch
        {
            return result;
        }

        if (appIds.Count == 0)
            return result;

        result.AppIds = [..appIds];
        result.HasSteamWarning = steamHasAppList;
        return result;
    }

    public async Task ResolveAndImportAppsAsync(List<string> appIds, Profile profile, IProgress<AppListProgressReport>? progress = null)
    {
        var allAppIds = new HashSet<string>(appIds);
        var allFoundDepotIds = new HashSet<string>();

        progress?.Report(new AppListProgressReport
        {
            Status = "Fetching package info...",
            IsIndeterminate = true,
            Current = 0,
            Total = appIds.Count
        });

        var packageInfos = new ConcurrentDictionary<string, AppPackageInfo?>();
        var semaphore = new SemaphoreSlim(6);
        var tasks = new List<Task>();
        var completedPackageFetches = 0;

        foreach (var id in appIds)
        {
            await semaphore.WaitAsync();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var info = await DepotService.FetchAppPackageInfoAsync(id).ConfigureAwait(false);
                    if (info != null)
                    {
                        packageInfos[id] = info;

                        foreach (var depot in info.Depots)
                            allFoundDepotIds.Add(depot);

                        foreach (var dlcPair in info.DlcDepots)
                        {
                            foreach (var depot in dlcPair.Value)
                                allFoundDepotIds.Add(depot);
                        }
                    }
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    Interlocked.Increment(ref completedPackageFetches);
                    progress?.Report(new AppListProgressReport
                    {
                        Status = $"Fetching package info ({completedPackageFetches}/{appIds.Count})...",
                        IsIndeterminate = false,
                        Current = completedPackageFetches,
                        Total = appIds.Count
                    });
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        tasks.Clear();
        semaphore = new SemaphoreSlim(6);

        var mainAppIdsToCreate = appIds
            .Where(id => !allFoundDepotIds.Contains(id))
            .ToList();

        progress?.Report(new AppListProgressReport
        {
            Status = $"Resolving game details (0/{mainAppIdsToCreate.Count})...",
            IsIndeterminate = false,
            Current = 0,
            Total = mainAppIdsToCreate.Count
        });

        var importedGames = new ConcurrentBag<Game>();
        var completedResolutions = 0;

        foreach (var id in mainAppIdsToCreate)
        {
            await semaphore.WaitAsync();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var info = await DepotService.FetchAppPackageInfoAsync(id).ConfigureAwait(false);
                    if (info == null) return;

                    var game = new Game { AppId = id, Name = string.Empty, Type = "Game" };

                    await SearchService.PopulateGameDetailsAsync(game).ConfigureAwait(false);

                    List<string>? depotsToAssign = null;

                    var parentGameInfo = packageInfos.Values.FirstOrDefault(p => p?.DlcAppIds.Contains(id) == true);

                    if (parentGameInfo != null)
                    {
                        if (parentGameInfo.DlcDepots.TryGetValue(id, out var dlcDepots))
                            depotsToAssign = dlcDepots;
                    }
                    else if (packageInfos.TryGetValue(id, out var selfInfo) && selfInfo != null)
                    {
                        if (selfInfo.Depots.Count > 0)
                            depotsToAssign = selfInfo.Depots;
                        else if (selfInfo.DlcDepots.TryGetValue(id, out var dlcDepots))
                            depotsToAssign = dlcDepots;
                    }

                    if (depotsToAssign != null)
                        game.Depots = depotsToAssign
                            .Where(depotId => allAppIds.Contains(depotId))
                            .ToList();

                    if (!string.IsNullOrWhiteSpace(game.IconUrl))
                    {
                        var path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId, game.IconUrl)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(path)) game.IconUrl = path;
                    }

                    importedGames.Add(game);
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    Interlocked.Increment(ref completedResolutions);
                    progress?.Report(new AppListProgressReport
                    {
                        Status = $"Resolving game details ({completedResolutions}/{mainAppIdsToCreate.Count})...",
                        IsIndeterminate = false,
                        Current = completedResolutions,
                        Total = mainAppIdsToCreate.Count
                    });
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);

        progress?.Report(new AppListProgressReport
        {
            Status = "Processing orphan depots...",
            IsIndeterminate = true,
            Current = 0,
            Total = 1
        });

        var newGames = importedGames.ToList();
        var depotsAddedCount = 0;

        foreach (var depotId in allAppIds.Where(id => allFoundDepotIds.Contains(id) && !mainAppIdsToCreate.Contains(id)))
        {
            string? parentAppId = null;

            foreach (var info in packageInfos.Values)
            {
                if (info == null) continue;
                if (info.Depots.Contains(depotId))
                {
                    parentAppId = info.AppId;
                    break;
                }

                foreach (var dlcDepotPair in info.DlcDepots)
                    if (dlcDepotPair.Value.Contains(depotId))
                    {
                        parentAppId = dlcDepotPair.Key;
                        break;
                    }

                if (parentAppId != null) break;
            }

            if (parentAppId != null)
            {
                var parentGame = _gameListController.Games.FirstOrDefault(g => g.AppId == parentAppId) ??
                                 newGames.FirstOrDefault(g => g.AppId == parentAppId);

                if (parentGame != null && !parentGame.Depots.Contains(depotId))
                {
                    parentGame.Depots.Add(depotId);
                    depotsAddedCount++;
                }
            }
        }

        progress?.Report(new AppListProgressReport
        {
            Status = "Adding games to profile...",
            IsIndeterminate = true,
            Current = 0,
            Total = 1
        });

        foreach (var game in newGames)
            profile.Games.Add(game);

        ProfileService.Save(profile);

        if (_profileController.CurrentProfile?.Name == "default" ||
            profile.Name == _profileController.CurrentProfile?.Name)
        {
            _gameListController.LoadGames(profile.Games);
        }

        var totalDepotsIncluded = newGames.Sum(g => g.Depots.Count) + depotsAddedCount;

        progress?.Report(new AppListProgressReport
        {
            Status = $"Done — added {newGames.Count} games",
            IsIndeterminate = false,
            Current = 1,
            Total = 1
        });

        _notificationManager.ShowToast($"Added {newGames.Count} Games/DLCs & {totalDepotsIncluded} Depots from {appIds.Count} IDs");
    }

    public async Task<int> GenerateAsync(Config? config, Profile? profile)
    {
        if (profile == null || config == null || string.IsNullOrWhiteSpace(config.GreenLumaPath))
            return -1;

        return await GreenLumaService.GenerateAppListAsync(profile, config);
    }

    public bool ValidatePathsForGeneration(Config? config)
    {
        if (config == null)
            return false;

        if (string.IsNullOrWhiteSpace(config.GreenLumaPath))
            return false;

        return _launcher.ValidatePaths(config);
    }
}

public class ImportResult
{
    public bool FoundAppList { get; set; }
    public bool FoundInSteamFolder { get; set; }
    public bool HasSteamWarning { get; set; }
    public List<string> AppIds { get; set; } = [];
}
