using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using GreenLuma_Manager.Models;

namespace GreenLuma_Manager.Services;

public class CacheEntry<T>
{
    public DateTime Expiry { get; set; }
    public T Data { get; set; } = default!;
}

public static class SteamApiCache
{
    internal static readonly ConcurrentDictionary<string, CacheEntry<object>> Cache = new();
    private static readonly ConcurrentDictionary<string, Task<object>> TaskCache = new();
    private static readonly TimeSpan CacheDurationLocal = TimeSpan.FromMinutes(30);
    private static DateTime _lastEviction = DateTime.MinValue;

    public static async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> fetchFunc)
    {
        EvictExpired();

        if (Cache.TryGetValue(key, out var entry))
            if (DateTime.Now < entry.Expiry && entry.Data is T cachedVal)
                return cachedVal;

        var task = TaskCache.GetOrAdd(key, _ => FetchAndCacheAsync(key, fetchFunc));
        try
        {
            var result = await task.ConfigureAwait(false);
            return (T)result;
        }
        finally
        {
            TaskCache.TryRemove(key, out _);
        }
    }

    private static void EvictExpired()
    {
        if ((DateTime.Now - _lastEviction).TotalMinutes < 5) return;
        _lastEviction = DateTime.Now;
        var expired = Cache.Where(kvp => DateTime.Now >= kvp.Value.Expiry).Select(kvp => kvp.Key).ToList();
        foreach (var key in expired)
            Cache.TryRemove(key, out _);
    }

    private static async Task<object> FetchAndCacheAsync<T>(string key, Func<Task<T>> fetchFunc)
    {
        var data = await fetchFunc().ConfigureAwait(false);

        if (data != null)
            Cache[key] = new CacheEntry<object>
            {
                Expiry = DateTime.Now.Add(CacheDurationLocal),
                Data = data
            };

        return data!;
    }
}

public class SearchService
{
    private const string SteamStoreSearchUrl =
        "https://store.steampowered.com/api/storesearch/?term={0}&l=english&cc=US";

    private const string SteamStoreApiUrl = "https://api.steampowered.com/IStoreService/GetAppList/v1/";
    private const int BatchSize = 150;
    private const int AppListPageSize = 50000;

    private static readonly string LocalLegacyPath =
        Path.Combine(AppContext.BaseDirectory, "Data", "steam_applist_legacy.json");

    private static string _steamApiKey = string.Empty;
    private static bool _showHiddenDlcs;
    private static List<SteamApp>? _appListCache;
    private static readonly SemaphoreSlim AppListLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, GameDetails> DetailsCache = new();
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private static bool _isPrefetching;

    public static void SetApiKey(string? apiKey)
    {
        _steamApiKey = apiKey ?? string.Empty;
    }

    public static void SetShowHiddenDlcs(bool show)
    {
        _showHiddenDlcs = show;
    }

    public static string? LookupAppName(string appId)
    {
        if (_appListCache == null) return null;
        var app = _appListCache.Find(a => a.AppId == appId);
        return app?.Name;
    }

    private static async Task<List<Game>> SearchStoreAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var url = string.Format(SteamStoreSearchUrl, Uri.EscapeDataString(query));
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
            var response = await HttpClientProvider.Default.GetStringAsync(url, linkedCts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (!root.TryGetProperty("items", out var items)) return [];

            var results = new List<Game>();

            foreach (var item in items.EnumerateArray())
            {
                var appId = item.TryGetProperty("id", out var idProp) ? idProp.ToString() : null;
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                var tinyImage = item.TryGetProperty("tiny_image", out var imgProp) ? imgProp.GetString() : null;

                if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(name))
                    results.Add(new Game
                    {
                        AppId = appId,
                        Name = name,
                        Type = "Game",
                        IconUrl = tinyImage ?? string.Empty
                    });
            }

            return results;
        }
        catch (Exception ex)
        {
            LogService.LogError("SearchService.SearchStore", ex);
            return [];
        }
    }

    private static async Task<List<SteamApp>> GetAppListAsync(CancellationToken ct = default)
    {
        if (_appListCache != null && DateTime.Now < _cacheExpiry)
            return _appListCache;

        await AppListLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_appListCache != null && DateTime.Now < _cacheExpiry)
                return _appListCache;

            var legacyTask = FetchLegacyAppListAsync(ct);
            var modernTask = string.IsNullOrWhiteSpace(_steamApiKey)
                ? Task.FromResult(new List<SteamApp>())
                : FetchModernAppListAsync(_steamApiKey, ct);

            await Task.WhenAll(legacyTask, modernTask).ConfigureAwait(false);

            _appListCache = MergeAppLists(legacyTask.Result, modernTask.Result);
            _cacheExpiry = DateTime.Now.Add(CacheDuration);
            return _appListCache;
        }
        catch (Exception ex)
        {
            LogService.LogError("SearchService.GetAppList", ex);
            return _appListCache ?? [];
        }
        finally
        {
            _isPrefetching = false;
            AppListLock.Release();
        }
    }

    private static async Task<List<SteamApp>> FetchLegacyAppListAsync(CancellationToken ct)
    {
        var results = new List<SteamApp>();
        try
        {
            if (!File.Exists(LocalLegacyPath)) return results;

            using var stream = new FileStream(LocalLegacyPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);
            var root = doc.RootElement;

            if (!root.TryGetProperty("applist", out var applist)) return results;
            if (!applist.TryGetProperty("apps", out var apps)) return results;

            foreach (var app in apps.EnumerateArray())
            {
                var appId = app.TryGetProperty("appid", out var aidProp) ? aidProp.ToString() : string.Empty;
                var name = app.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty;

                if (!string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(name))
                    results.Add(new SteamApp(appId, name, name.ToLowerInvariant()));
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("SearchService.FetchLegacyAppList", ex);
        }

        return results;
    }

    private static async Task<List<SteamApp>> FetchModernAppListAsync(string apiKey, CancellationToken ct)
    {
        var results = new List<SteamApp>();
        try
        {
            uint lastAppId = 0;
            while (true)
            {
                var url =
                    $"{SteamStoreApiUrl}?key={Uri.EscapeDataString(apiKey)}&include_games=true&include_dlc=true&include_software=true&include_videos=true&include_hardware=true&max_results={AppListPageSize}&last_appid={lastAppId}";

                var response = await HttpClientProvider.Default.GetStringAsync(url, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (!root.TryGetProperty("response", out var responseElem)) break;
                if (!responseElem.TryGetProperty("apps", out var apps)) break;

                var hasApps = false;
                foreach (var app in apps.EnumerateArray())
                {
                    hasApps = true;
                    var appId = app.TryGetProperty("appid", out var aidProp) ? aidProp.ToString() : string.Empty;
                    var name = app.TryGetProperty("name", out var nProp)
                        ? nProp.GetString() ?? string.Empty
                        : string.Empty;

                    if (!string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(name))
                        results.Add(new SteamApp(appId, name, name.ToLowerInvariant()));
                }

                if (!hasApps) break;

                var haveMore = responseElem.TryGetProperty("have_more_results", out var hmProp) &&
                               hmProp.ValueKind == JsonValueKind.True;
                if (!haveMore) break;

                if (responseElem.TryGetProperty("last_appid", out var laProp) &&
                    laProp.TryGetUInt32(out var newLastAppId))
                    lastAppId = newLastAppId;
                else
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("SearchService.FetchModernAppList", ex);
        }

        return results;
    }

    private static List<SteamApp> MergeAppLists(List<SteamApp> legacy, List<SteamApp> modern)
    {
        var merged = new Dictionary<string, SteamApp>(legacy.Count + modern.Count);

        foreach (var app in legacy)
            merged[app.AppId] = app;

        foreach (var app in modern)
            merged[app.AppId] = app;

        try
        {
            return [.. merged.Values.OrderBy(a => uint.TryParse(a.AppId, out var id) ? id : uint.MaxValue)];
        }
        catch (Exception ex)
        {
            LogService.LogError("SearchService.MergeAppLists", ex);
            return [.. merged.Values];
        }
    }

    public static async Task PrefetchAsync(Config config)
    {
        if (_isPrefetching || (_appListCache != null && DateTime.Now < _cacheExpiry) || !config.PrefetchAppList)
            return;

        _isPrefetching = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await GetAppListAsync().ConfigureAwait(false);
            }
            catch
            {
                _isPrefetching = false;
            }
        });
    }

    public static async Task<List<Game>> SearchAsync(string query, int maxResults = 200, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return [];

        try
        {
            var queryLower = query.ToLower();
            var cacheKey = $"search:{queryLower}:{maxResults}";

            var cached = await SteamApiCache.GetOrAddAsync(cacheKey, async () =>
            {
                if (uint.TryParse(query, out _))
                {
                    var detailsMap = await FetchGameDetailsBatchAsync([query]).ConfigureAwait(false);
                    if (detailsMap.TryGetValue(query, out var details) &&
                        !string.IsNullOrEmpty(details.Name) &&
                        details.Name != $"App {query}")
                        return
                        [
                            new Game
                            {
                                AppId = query,
                                Name = details.Name,
                                Type = details.Type,
                                IconUrl = string.Empty
                            }
                        ];
                }

                var storeTask = SearchStoreAsync(query, ct);

                var appList = _appListCache;
                List<Game> localResults = [];
                if (appList is { Count: > 0 } && DateTime.Now < _cacheExpiry)
                    localResults = appList
                        .Select(app => (app, score: CalculateScore(app.NameLower, queryLower)))
                        .Where(x => x.score > 0)
                        .OrderByDescending(x => x.score)
                        .ThenBy(x => x.app.Name.Length)
                        .Take(maxResults)
                        .Select(x => new Game { AppId = x.app.AppId, Name = x.app.Name, Type = "Game" })
                        .ToList();

                var smartResults = await storeTask.ConfigureAwait(false);

                var finalResults = new List<Game>(localResults);
                var existingIds = new HashSet<string>(localResults.Select(g => g.AppId));

                foreach (var game in smartResults)
                {
                    if (finalResults.Count >= maxResults) break;

                    if (!existingIds.Contains(game.AppId))
                    {
                        finalResults.Add(game);
                        existingIds.Add(game.AppId);
                    }
                }

                return finalResults;
            }).ConfigureAwait(false);

            return
            [
                .. cached.Select(g => new Game { AppId = g.AppId, Name = g.Name, Type = g.Type, IconUrl = g.IconUrl })
            ];
        }
        catch (Exception ex)
        {
            LogService.LogError("SearchService.SearchAsync", ex);
            return [];
        }
    }

    private static int CalculateScore(string nameLower, string query)
    {
        if (string.IsNullOrEmpty(nameLower))
            return 0;

        var score = 0;

        if (nameLower == query)
            return 10000;

        if (nameLower.StartsWith(query))
            score += 5000;

        var nameWords = nameLower.Split([' ', '-', ':', '_', '™', '®'], StringSplitOptions.RemoveEmptyEntries);
        var queryWords = query.Split([' '], StringSplitOptions.RemoveEmptyEntries);

        if (nameWords.Length > 0 && nameWords[0].StartsWith(query))
            score += 3000;

        var matchingWords = queryWords.Count(queryWord => nameWords.Any(w => w.StartsWith(queryWord)));

        if (queryWords.Length > 1 && matchingWords == queryWords.Length)
            score += 2000;

        if (nameLower.Contains(query))
            score += 1000;

        var lengthPenalty = Math.Max(0, (nameLower.Length - query.Length) * 50);
        score -= lengthPenalty;

        if (HasWordBoundaryMatch(nameLower, query))
            score += 500;

        if (ContainsAllCharsInOrder(nameLower, query))
            score += 100;

        return Math.Max(0, score);
    }

    private static bool HasWordBoundaryMatch(string name, string query)
    {
        var words = name.Split([' ', '-', ':', '_'], StringSplitOptions.RemoveEmptyEntries);
        return words.Any(w => w.StartsWith(query));
    }

    private static bool ContainsAllCharsInOrder(string text, string chars)
    {
        var charIndex = 0;
        foreach (var c in text)
            if (charIndex < chars.Length && c == chars[charIndex])
                charIndex++;
        return charIndex == chars.Length;
    }

    private static void EvictExpiredDetails()
    {
        if (DetailsCache.Count > 10000)
        {
            var keys = DetailsCache.Keys.Take(5000).ToList();
            foreach (var key in keys)
                DetailsCache.TryRemove(key, out _);
        }
    }

    private static async Task<Dictionary<string, GameDetails>> FetchGameDetailsBatchAsync(List<string> appIds)
    {
        EvictExpiredDetails();
        var results = new Dictionary<string, GameDetails>();
        var uncachedAppIds = new List<string>();

        foreach (var appId in appIds)
        {
            if (DetailsCache.TryGetValue(appId, out var memDetails))
            {
                results[appId] = memDetails;
                continue;
            }

            var key = $"details:{appId}";
            if (SteamApiCache.Cache.TryGetValue(key, out var entry) &&
                DateTime.Now < entry.Expiry &&
                entry.Data is GameDetails cached)
            {
                results[appId] = cached;
                DetailsCache.TryAdd(appId, cached);
            }
            else
            {
                uncachedAppIds.Add(appId);
            }
        }

        if (uncachedAppIds.Count == 0)
            return results;

        var batches = uncachedAppIds.Chunk(BatchSize).ToList();

        foreach (var batch in batches)
            try
            {
                var validAppIds = batch.Where(id => uint.TryParse(id, out _)).ToList();
                if (validAppIds.Count == 0) continue;

                var batchResults =
                    await SteamService.Instance.GetAppInfoBatchAsync(validAppIds.Select(uint.Parse).ToList())
                        .ConfigureAwait(false);

                foreach (var (appId, details) in batchResults)
                {
                    var appIdStr = appId.ToString();
                    results[appIdStr] = details;

                    var key = $"details:{appIdStr}";
                    SteamApiCache.Cache[key] = new CacheEntry<object>
                    {
                        Expiry = DateTime.Now.Add(TimeSpan.FromMinutes(30)),
                        Data = details
                    };
                    DetailsCache.TryAdd(appIdStr, details);
                }

                foreach (var appIdStr in validAppIds.Where(id => !results.ContainsKey(id)))
                {
                    var fallbackDetails = new GameDetails(appIdStr, "Game", $"App {appIdStr}");
                    results[appIdStr] = fallbackDetails;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("SearchService.FetchBatch", ex);
                foreach (var appIdStr in batch)
                    if (!results.ContainsKey(appIdStr))
                        results[appIdStr] = new GameDetails(appIdStr, "Game", $"App {appIdStr}");
            }

        return results;
    }

    public static async Task PopulateGameDetailsAsync(Game game, CancellationToken ct = default)
    {
        var detailsMap = await FetchGameDetailsBatchAsync([game.AppId]).ConfigureAwait(false);
        if (detailsMap.TryGetValue(game.AppId, out var details))
        {
            if (details.Name != $"App {game.AppId}")
                game.Name = details.Name;
            game.Type = details.Type;

            var iconPath = await IconCacheService.CacheIconForGameAsync(details).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(iconPath))
                game.IconUrl = iconPath;
        }
    }

    public static async Task FetchIconUrlAsync(Game game, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(game.IconUrl) && !game.IconUrl.StartsWith("http"))
            return;

        await PopulateGameDetailsAsync(game, ct).ConfigureAwait(false);
    }

    public static async Task FetchIconUrlsAsync(List<Game> games, CancellationToken ct = default)
    {
        var appIds = games.Select(g => g.AppId).Distinct().ToList();
        var detailsMap = await FetchGameDetailsBatchAsync(appIds).ConfigureAwait(false);

        foreach (var game in games)
            if (detailsMap.TryGetValue(game.AppId, out var details))
            {
                if (!string.IsNullOrEmpty(details.Name) && details.Name != $"App {game.AppId}")
                    game.Name = details.Name;
                game.Type = details.Type;
            }

        var semaphore = new SemaphoreSlim(8);
        var tasks = games.Select(async game =>
        {
            if (detailsMap.TryGetValue(game.AppId, out var details))
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var iconPath = await IconCacheService.CacheIconForGameAsync(details).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(iconPath))
                        game.IconUrl = iconPath;
                }
                finally
                {
                    semaphore.Release();
                }
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static async Task ExpandHiddenDlcsAsync(List<Game> results, CancellationToken ct = default)
    {
        if (!_showHiddenDlcs) return;

        var gameAppIds = results
            .Where(g => string.Equals(g.Type, "Game", StringComparison.OrdinalIgnoreCase))
            .Select(g => g.AppId)
            .Distinct()
            .ToList();

        if (gameAppIds.Count == 0) return;

        var detailsMap = await FetchGameDetailsBatchAsync(gameAppIds).ConfigureAwait(false);

        var existingIds = new HashSet<string>(results.Select(g => g.AppId));
        var hiddenDlcIds = new List<string>();
        var dlcParentName = new Dictionary<string, string>();

        foreach (var appId in gameAppIds)
        {
            if (!detailsMap.TryGetValue(appId, out var details)) continue;
            if (details.ListOfDlc == null || details.ListOfDlc.Count == 0) continue;

            foreach (var dlcId in details.ListOfDlc)
                if (!existingIds.Contains(dlcId))
                {
                    hiddenDlcIds.Add(dlcId);
                    existingIds.Add(dlcId);
                    dlcParentName.TryAdd(dlcId, details.Name);
                }
        }

        foreach (var appIdStr in gameAppIds)
        {
            if (ct.IsCancellationRequested) return;
            if (!uint.TryParse(appIdStr, out var appIdNum)) continue;

            var parentName = detailsMap.TryGetValue(appIdStr, out var parentDetails) ? parentDetails.Name : null;

            try
            {
                var (storePackageIds, storeDlcIds) =
                    await FetchStorePackageAndDlcIdsAsync(appIdStr, ct).ConfigureAwait(false);

                foreach (var dlcId in storeDlcIds)
                    if (!existingIds.Contains(dlcId))
                    {
                        hiddenDlcIds.Add(dlcId);
                        existingIds.Add(dlcId);
                        if (parentName != null)
                            dlcParentName.TryAdd(dlcId, parentName);
                    }

                var picsPackageIds = await SteamService.Instance.GetAppPackageIdsAsync(appIdNum).ConfigureAwait(false);
                var allPackageIds = new HashSet<uint>(storePackageIds);
                foreach (var pkgId in picsPackageIds)
                    allPackageIds.Add(pkgId);

                foreach (var pkgId in allPackageIds)
                {
                    if (ct.IsCancellationRequested) return;

                    var pkgAppIds = await SteamService.Instance.GetPackageAppIdsAsync(pkgId).ConfigureAwait(false);
                    foreach (var pkgAppId in pkgAppIds)
                    {
                        var pkgAppIdStr = pkgAppId.ToString();
                        if (!existingIds.Contains(pkgAppIdStr))
                        {
                            hiddenDlcIds.Add(pkgAppIdStr);
                            existingIds.Add(pkgAppIdStr);
                            if (parentName != null)
                                dlcParentName.TryAdd(pkgAppIdStr, parentName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("SearchService.ExpandPackages", ex);
            }
        }

        if (_appListCache != null)
        {
            var gameNames = new Dictionary<string, string>();
            foreach (var appIdStr in gameAppIds)
                if (detailsMap.TryGetValue(appIdStr, out var d) && !string.IsNullOrEmpty(d.Name))
                    gameNames.TryAdd(d.Name.ToLowerInvariant(), d.Name);

            foreach (var app in _appListCache)
            {
                if (existingIds.Contains(app.AppId)) continue;

                foreach (var (gameNameLower, gameName) in gameNames)
                    if (app.NameLower.StartsWith(gameNameLower) && app.NameLower.Length > gameNameLower.Length)
                    {
                        var charAfter = app.NameLower[gameNameLower.Length];
                        if (charAfter is ' ' or '-' or ':' or '–' or '—' or '(' or '[')
                        {
                            hiddenDlcIds.Add(app.AppId);
                            existingIds.Add(app.AppId);
                            dlcParentName.TryAdd(app.AppId, gameName);
                            break;
                        }
                    }
            }
        }

        foreach (var appIdStr in gameAppIds)
        {
            if (ct.IsCancellationRequested) return;
            if (!uint.TryParse(appIdStr, out var baseAppId)) continue;

            var knownDlcIds = new List<uint>();
            if (detailsMap.TryGetValue(appIdStr, out var baseDetails) && baseDetails.ListOfDlc != null)
                foreach (var dlcId in baseDetails.ListOfDlc)
                    if (uint.TryParse(dlcId, out var dlcIdNum))
                        knownDlcIds.Add(dlcIdNum);

            foreach (var id in hiddenDlcIds)
                if (uint.TryParse(id, out var idNum))
                    knownDlcIds.Add(idNum);

            if (knownDlcIds.Count == 0) continue;

            var parentName = detailsMap.TryGetValue(appIdStr, out var pd) ? pd.Name : null;

            try
            {
                var rangeDlcIds = await SteamService.Instance.ScanRangeForDlcsAsync(baseAppId, knownDlcIds)
                    .ConfigureAwait(false);

                foreach (var dlcId in rangeDlcIds)
                {
                    var dlcIdStr = dlcId.ToString();
                    if (!existingIds.Contains(dlcIdStr))
                    {
                        hiddenDlcIds.Add(dlcIdStr);
                        existingIds.Add(dlcIdStr);
                        if (parentName != null)
                            dlcParentName.TryAdd(dlcIdStr, parentName);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("SearchService.ExpandRangeScan", ex);
            }
        }

        if (hiddenDlcIds.Count == 0) return;

        var appListLookup = new Dictionary<string, string>();
        if (_appListCache != null)
            foreach (var app in _appListCache)
                appListLookup.TryAdd(app.AppId, app.Name);

        var dlcDetailsMap = await FetchGameDetailsBatchAsync(hiddenDlcIds).ConfigureAwait(false);

        var unresolvedIds = new List<string>();
        foreach (var dlcId in hiddenDlcIds)
        {
            var hasPics = dlcDetailsMap.TryGetValue(dlcId, out var d) && d.Name != $"App {dlcId}";
            var hasCache = appListLookup.ContainsKey(dlcId);
            if (!hasPics && !hasCache)
                unresolvedIds.Add(dlcId);
        }

        if (unresolvedIds.Count > 0 && !string.IsNullOrWhiteSpace(_steamApiKey))
        {
            var browseResults = await FetchStoreBrowseItemsAsync(unresolvedIds, ct).ConfigureAwait(false);
            foreach (var (id, name) in browseResults)
                appListLookup[id] = name;
        }

        foreach (var dlcId in hiddenDlcIds)
        {
            if (ct.IsCancellationRequested) return;

            string name;
            string type;

            if (dlcDetailsMap.TryGetValue(dlcId, out var dlcDetails) && dlcDetails.Name != $"App {dlcId}")
            {
                name = dlcDetails.Name;
                type = dlcDetails.Type;
            }
            else if (appListLookup.TryGetValue(dlcId, out var cachedName) && !string.IsNullOrEmpty(cachedName))
            {
                name = cachedName;
                type = dlcDetailsMap.TryGetValue(dlcId, out var d) ? d.Type : "DLC";
            }
            else
            {
                name = dlcParentName.TryGetValue(dlcId, out var parentName)
                    ? $"{parentName} - DLC {dlcId}"
                    : $"Unknown DLC {dlcId}";
                type = "DLC";
            }

            results.Add(new Game
            {
                AppId = dlcId,
                Name = name,
                Type = type,
                IconUrl = string.Empty
            });
        }
    }

    private static async Task<(List<uint> PackageIds, List<string> DlcIds)> FetchStorePackageAndDlcIdsAsync(
        string appId, CancellationToken ct = default)
    {
        var packageIds = new List<uint>();
        var dlcIds = new List<string>();
        try
        {
            var url =
                $"https://store.steampowered.com/api/appdetails?appids={Uri.EscapeDataString(appId)}&cc=US&l=english";
            var response = await HttpClientProvider.Default.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty(appId, out var appElem)) return (packageIds, dlcIds);
            if (!appElem.TryGetProperty("success", out var success) || !success.GetBoolean())
                return (packageIds, dlcIds);
            if (!appElem.TryGetProperty("data", out var data)) return (packageIds, dlcIds);

            if (data.TryGetProperty("packages", out var packages))
                foreach (var pkg in packages.EnumerateArray())
                    if (pkg.TryGetUInt32(out var pkgId))
                        packageIds.Add(pkgId);

            if (data.TryGetProperty("dlc", out var dlcArray))
                foreach (var dlc in dlcArray.EnumerateArray())
                    if (dlc.TryGetUInt32(out var dlcId))
                        dlcIds.Add(dlcId.ToString());
        }
        catch (Exception ex)
        {
            LogService.LogError("SearchService.FetchStorePackageAndDlcIds", ex);
        }

        return (packageIds, dlcIds);
    }

    public static async Task<Dictionary<string, GameDetails>> FetchStoreAppDetailsAsync(
        List<string> appIds, CancellationToken ct = default)
    {
        var results = new Dictionary<string, GameDetails>();
        var semaphore = new SemaphoreSlim(2);

        var tasks = appIds.Select(async appId =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var url =
                    $"https://store.steampowered.com/api/appdetails?appids={Uri.EscapeDataString(appId)}&cc=US&l=english";
                var response = await HttpClientProvider.Default.GetStringAsync(url, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(response);

                if (!doc.RootElement.TryGetProperty(appId, out var appElem)) return;
                if (!appElem.TryGetProperty("success", out var success) || !success.GetBoolean()) return;
                if (!appElem.TryGetProperty("data", out var data)) return;

                var name = data.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? $"App {appId}"
                    : $"App {appId}";
                var typeStr = data.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "dlc" : "dlc";

                var type = typeStr.ToLower() switch
                {
                    "game" => "Game",
                    "dlc" => "DLC",
                    "demo" => "Demo",
                    "mod" => "Mod",
                    "video" => "Video",
                    "music" => "Soundtrack",
                    _ => "DLC"
                };

                lock (results)
                {
                    results[appId] = new GameDetails(appId, type, name);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"SearchService.FetchStoreAppDetails:{appId}", ex);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static async Task<Dictionary<string, string>> FetchStoreBrowseItemsAsync(
        List<string> appIds, CancellationToken ct = default)
    {
        var results = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(_steamApiKey)) return results;

        try
        {
            var ids = appIds
                .Where(id => uint.TryParse(id, out _))
                .Select(id => new { appid = uint.Parse(id) })
                .ToArray();

            var inputJson = JsonSerializer.Serialize(new
            {
                ids = ids.Select(x => new { x.appid }).ToArray(),
                context = new { language = "english", country_code = "US" },
                data_request = new { include_basic_info = true }
            });

            var url =
                $"https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?key={Uri.EscapeDataString(_steamApiKey)}&input_json={Uri.EscapeDataString(inputJson)}";
            var response = await HttpClientProvider.Default.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("response", out var resp)) return results;
            if (!resp.TryGetProperty("store_items", out var items)) return results;

            foreach (var item in items.EnumerateArray())
            {
                var appId = item.TryGetProperty("appid", out var aidProp) ? aidProp.GetUInt32().ToString() : null;
                var name = item.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;

                if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(name))
                    results[appId] = name;
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("SearchService.FetchStoreBrowseItems", ex);
        }

        return results;
    }

    public static async Task<Dictionary<string, string>> ResolveAppNamesAsync(List<string> appIds)
    {
        var results = new Dictionary<string, string>();
        var unresolved = new List<string>();

        foreach (var id in appIds)
        {
            var name = LookupAppName(id);
            if (name != null)
                results[id] = name;
            else
                unresolved.Add(id);
        }

        if (unresolved.Count > 0)
        {
            var browseResults = await FetchStoreBrowseItemsAsync(unresolved).ConfigureAwait(false);
            foreach (var (id, name) in browseResults)
                results[id] = name;
        }

        return results;
    }

    private record SteamApp(string AppId, string Name, string NameLower);
}