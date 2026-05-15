using System.Collections.Concurrent;
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

    private static string _steamApiKey = string.Empty;
    private static bool _showHiddenDlcs;
    private static List<SteamApp>? _appListCache;
    private static readonly SemaphoreSlim AppListLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, GameDetails> DetailsCache = new();
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private static bool _isPrefetching;

    public static void SetApiKey(string apiKey)
    {
        _steamApiKey = apiKey ?? string.Empty;
    }

    public static void SetShowHiddenDlcs(bool show)
    {
        _showHiddenDlcs = show;
    }

    private static async Task<List<Game>> SearchStoreAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var url = string.Format(SteamStoreSearchUrl, Uri.EscapeDataString(query));
            var response = await HttpClientProvider.Default.GetStringAsync(url, ct).ConfigureAwait(false);
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
        if (string.IsNullOrWhiteSpace(_steamApiKey))
            return _appListCache ?? [];

        if (_appListCache != null && DateTime.Now < _cacheExpiry)
            return _appListCache;

        await AppListLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_appListCache != null && DateTime.Now < _cacheExpiry)
                return _appListCache;

            _appListCache = [];
            uint lastAppId = 0;
            const int maxResults = 50000;

            while (true)
            {
                var url =
                    $"{SteamStoreApiUrl}?key={Uri.EscapeDataString(_steamApiKey)}&include_games=true&include_dlc=true&include_software=true&include_videos=true&include_hardware=true&max_results={maxResults}&last_appid={lastAppId}";

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
                    var name = app.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty;

                    if (!string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(name))
                        _appListCache.Add(new SteamApp(appId, name, name.ToLowerInvariant()));
                }

                if (!hasApps) break;

                var haveMore = responseElem.TryGetProperty("have_more_results", out var hmProp) &&
                               hmProp.ValueKind == JsonValueKind.True;
                if (!haveMore)
                    break;

                if (responseElem.TryGetProperty("last_appid", out var laProp) && laProp.TryGetUInt32(out var newLastAppId))
                    lastAppId = newLastAppId;
                else
                    break;
            }

            _cacheExpiry = DateTime.Now.Add(CacheDuration);
            _isPrefetching = false;
            return _appListCache;
        }
        catch (Exception ex)
        {
            _isPrefetching = false;
            LogService.LogError("SearchService.GetAppList", ex);
            return _appListCache ?? [];
        }
        finally
        {
            AppListLock.Release();
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

                var localTask = Task.Run(async () =>
                {
                    var appList = await GetAppListAsync(ct).ConfigureAwait(false);
                    if (appList.Count == 0) return [];

                    return appList
                        .Select(app => (app, score: CalculateScore(app.NameLower, queryLower)))
                        .Where(x => x.score > 0)
                        .OrderByDescending(x => x.score)
                        .ThenBy(x => x.app.Name.Length)
                        .Take(maxResults)
                        .Select(x => new Game
                        {
                            AppId = x.app.AppId,
                            Name = x.app.Name,
                            Type = "Game"
                        })
                        .ToList();
                });

                await Task.WhenAll(storeTask, localTask).ConfigureAwait(false);

                var smartResults = storeTask.Result;
                var localResults = localTask.Result;

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

        foreach (var appId in gameAppIds)
        {
            if (!detailsMap.TryGetValue(appId, out var details)) continue;
            if (details.ListOfDlc == null || details.ListOfDlc.Count == 0) continue;

            foreach (var dlcId in details.ListOfDlc)
            {
                if (!existingIds.Contains(dlcId))
                {
                    hiddenDlcIds.Add(dlcId);
                    existingIds.Add(dlcId);
                }
            }
        }

        if (hiddenDlcIds.Count == 0) return;

        var dlcDetailsMap = await FetchGameDetailsBatchAsync(hiddenDlcIds).ConfigureAwait(false);

        foreach (var dlcId in hiddenDlcIds)
        {
            if (ct.IsCancellationRequested) return;

            var name = dlcDetailsMap.TryGetValue(dlcId, out var dlcDetails)
                ? dlcDetails.Name
                : $"App {dlcId}";
            var type = dlcDetailsMap.TryGetValue(dlcId, out var dlcDetails2)
                ? dlcDetails2.Type
                : "DLC";

            results.Add(new Game
            {
                AppId = dlcId,
                Name = name,
                Type = type,
                IconUrl = string.Empty
            });
        }
    }

    private record SteamApp(string AppId, string Name, string NameLower);
}