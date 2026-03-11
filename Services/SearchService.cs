using System.Collections.Concurrent;
using System.Net.Http;
using GreenLuma_Manager.Models;
using Newtonsoft.Json.Linq;

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

    public static async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> fetchFunc)
    {
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
    private const string SteamApiKey = "1DD0450A99F573693CD031EBB160907D";
    private const int BatchSize = 150;

    private static readonly HttpClient Client = new();
    private static List<SteamApp>? _appListCache;
    private static readonly SemaphoreSlim AppListLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, GameDetails> DetailsCache = new();
    private static DateTime _cacheExpiry = DateTime.MaxValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private static bool _isPrefetching;

    static SearchService()
    {
        Client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        Client.Timeout = TimeSpan.FromSeconds(10);
    }

    private static async Task<List<Game>> SearchStoreAsync(string query)
    {
        try
        {
            var url = string.Format(SteamStoreSearchUrl, Uri.EscapeDataString(query));
            var response = await Client.GetStringAsync(url).ConfigureAwait(false);
            var json = JObject.Parse(response);

            var items = json["items"];
            if (items == null) return [];

            var results = new List<Game>();

            foreach (var item in items)
            {
                var appId = item["id"]?.ToString();
                var name = item["name"]?.ToString();
                var tinyImage = item["tiny_image"]?.ToString();

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
        catch
        {
            return [];
        }
    }

    private static async Task<List<SteamApp>> GetAppListAsync()
    {
        if (_appListCache != null && DateTime.Now < _cacheExpiry)
            return _appListCache;

        await AppListLock.WaitAsync().ConfigureAwait(false);
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
                    $"{SteamStoreApiUrl}?key={SteamApiKey}&include_games=true&include_dlc=true&include_software=true&include_videos=true&include_hardware=true&max_results={maxResults}&last_appid={lastAppId}";

                var response = await Client.GetStringAsync(url).ConfigureAwait(false);
                var json = JObject.Parse(response);
                var apps = json["response"]?["apps"];

                if (apps == null || !apps.Any())
                    break;

                foreach (var app in apps)
                {
                    var appId = app["appid"]?.ToString() ?? string.Empty;
                    var name = app["name"]?.ToString() ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(name))
                        _appListCache.Add(new SteamApp(appId, name));
                }

                var haveMore = json["response"]?["have_more_results"]?.Value<bool>() ?? false;
                if (!haveMore)
                    break;

                var lastAppIdFromResponse = json["response"]?["last_appid"]?.Value<uint>();
                if (lastAppIdFromResponse.HasValue)
                    lastAppId = lastAppIdFromResponse.Value;
                else
                    break;
            }

            _cacheExpiry = DateTime.Now.Add(CacheDuration);
            _isPrefetching = false;
            return _appListCache;
        }
        catch
        {
            _isPrefetching = false;
            return _appListCache ?? [];
        }
        finally
        {
            AppListLock.Release();
        }
    }

    public static async Task PrefetchAsync()
    {
        if (_isPrefetching || (_appListCache != null && DateTime.Now < _cacheExpiry))
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

    public static async Task<List<Game>> SearchAsync(string query, int maxResults = 50)
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

                var storeTask = SearchStoreAsync(query);

                var localTask = Task.Run(async () =>
                {
                    var appList = await GetAppListAsync().ConfigureAwait(false);
                    if (appList.Count == 0) return [];

                    return appList
                        .Select(app => (app, score: CalculateScore(app.Name, queryLower)))
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

                var finalResults = new List<Game>(smartResults);
                var existingIds = new HashSet<string>(smartResults.Select(g => g.AppId));

                foreach (var game in localResults)
                {
                    if (finalResults.Count >= maxResults) break;

                    if (!existingIds.Contains(game.AppId))
                    {
                        finalResults.Add(game);
                        existingIds.Add(game.AppId);
                    }
                }

                return finalResults;
            });

            return
            [
                .. cached.Select(g => new Game { AppId = g.AppId, Name = g.Name, Type = g.Type, IconUrl = g.IconUrl })
            ];
        }
        catch
        {
            return [];
        }
    }

    private static int CalculateScore(string appName, string query)
    {
        if (string.IsNullOrEmpty(appName))
            return 0;

        var nameLower = appName.ToLower();
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

        var lengthPenalty = Math.Max(0, (appName.Length - query.Length) * 50);
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

    private static async Task<Dictionary<string, GameDetails>> FetchGameDetailsBatchAsync(List<string> appIds)
    {
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
            catch
            {
                foreach (var appIdStr in batch)
                    if (!results.ContainsKey(appIdStr))
                        results[appIdStr] = new GameDetails(appIdStr, "Game", $"App {appIdStr}");
            }

        return results;
    }

    public static async Task PopulateGameDetailsAsync(Game game)
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

    public static async Task FetchIconUrlAsync(Game game)
    {
        if (!string.IsNullOrEmpty(game.IconUrl) && !game.IconUrl.StartsWith("http"))
            return;

        await PopulateGameDetailsAsync(game).ConfigureAwait(false);
    }

    public static async Task FetchIconUrlsAsync(List<Game> games)
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
                await semaphore.WaitAsync().ConfigureAwait(false);
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

    private record SteamApp(string AppId, string Name);
}