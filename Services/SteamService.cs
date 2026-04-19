using SteamKit2;

namespace GreenLuma_Manager.Services;

public sealed class SteamService : IDisposable
{
    private static readonly Lazy<SteamService> InstanceHolder = new(() => new SteamService());

    private readonly Task _callbackLoop;
    private readonly CallbackManager _callbackManager;
    private readonly TaskCompletionSource _connectedTcs;
    private readonly CancellationTokenSource _cts;
    private readonly TaskCompletionSource _loggedOnTcs;
    private readonly SteamApps _steamApps;
    private readonly SteamClient _steamClient;
    private readonly SteamUser _steamUser;

    private bool _isConnected;
    private bool _isLoggedOn;
    private bool _isRunning;

    private SteamService()
    {
        _steamClient = new SteamClient();
        _callbackManager = new CallbackManager(_steamClient);
        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamApps = _steamClient.GetHandler<SteamApps>()!;

        _cts = new CancellationTokenSource();
        _connectedTcs = new TaskCompletionSource();
        _loggedOnTcs = new TaskCompletionSource();

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);

        _isRunning = true;
        _callbackLoop = Task.Run(CallbackLoop);

        _steamClient.Connect();
    }

    public static SteamService Instance => InstanceHolder.Value;

    public void Dispose()
    {
        _isRunning = false;
        _cts.Cancel();
        _steamClient.Disconnect();
        try
        {
            _callbackLoop.Wait(1000);
        }
        catch (Exception ex)
        {
            LogService.LogError("SteamService.Dispose", ex);
        }

        _cts.Dispose();
    }

    public async Task<GameDetails?> GetGameDetailsAsync(uint appId)
    {
        var result = await GetAppInfoBatchAsync([appId]).ConfigureAwait(false);
        return result.GetValueOrDefault(appId);
    }

    public async Task<Dictionary<uint, GameDetails>> GetAppInfoBatchAsync(List<uint> appIds)
    {
        var results = new Dictionary<uint, GameDetails>();
        var maxRetries = 2;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
            try
            {
                await EnsureReadyAsync().ConfigureAwait(false);

                var requests = appIds.Select(id => new SteamApps.PICSRequest { ID = id, AccessToken = 0 }).ToList();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                var job = _steamApps.PICSGetProductInfo(requests, []);
                var task = job.ToTask();

                if (await Task.WhenAny(task, Task.Delay(5000, cts.Token)) != task)
                {
                    if (attempt < maxRetries) continue;
                    break;
                }

                var result = await task.ConfigureAwait(false);

                if (result.Failed || result.Results == null)
                {
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(500, cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    return results;
                }

                foreach (var callback in result.Results)
                foreach (var (appId, appData) in callback.Apps)
                {
                    var kv = appData.KeyValues;
                    var common = kv["common"];

                    var name = common["name"].Value ?? $"App {appId}";
                    var type = MapSteamTypeToDisplayType(common["type"].Value ?? "Game");

                    var clientIconHash = common["clienticon"].Value;
                    var parentId = common["parent"].Value;

                    var libAssets = common["library_assets"];
                    var heroHash = libAssets["hero_capsule"]["image"].Value;

                    var assets = common["assets"];
                    var mainHash = assets["main_capsule"]["image"].Value;

                    var headerNode = common["header_image"];
                    var headerImage = headerNode.Value;
                    if (string.IsNullOrEmpty(headerImage))
                        headerImage = headerNode["english"].Value;

                    results[appId] = new GameDetails(
                        appId.ToString(),
                        type,
                        name,
                        clientIconHash,
                        heroHash,
                        mainHash,
                        parentId,
                        headerImage
                    );
                }

                if (results.Count > 0) return results;
            }
            catch (Exception ex)
            {
                LogService.LogError("SteamService.GetAppInfoBatch", ex);
                if (attempt == maxRetries) break;
                await Task.Delay(500).ConfigureAwait(false);
            }

        return results;
    }

    public async Task<AppPackageInfo?> GetAppPackageInfoAsync(uint appId)
    {
        try
        {
            await EnsureReadyAsync().ConfigureAwait(false);

            var request = new SteamApps.PICSRequest { ID = appId, AccessToken = 0 };
            var job = _steamApps.PICSGetProductInfo([request], []);

            var result = await job.ToTask().ConfigureAwait(false);

            if (result.Failed || result.Results == null)
                return null;

            foreach (var callback in result.Results)
                if (callback.Apps.TryGetValue(appId, out var appData))
                    return ParseAppPackageInfo(appId, appData);

            return null;
        }
        catch (Exception ex)
        {
            LogService.LogError("SteamService.GetAppPackageInfo", ex);
            return null;
        }
    }

    private static AppPackageInfo? ParseAppPackageInfo(uint appId,
        SteamApps.PICSProductInfoCallback.PICSProductInfo appData)
    {
        var kv = appData.KeyValues;

        var type = kv["common"]["type"].Value;
        if (string.Equals(type, "depot", StringComparison.OrdinalIgnoreCase))
            return null;

        var info = new AppPackageInfo
        {
            AppId = appId.ToString()
        };

        var dlcList = kv["common"]["extended"]["listofdlc"].Value;
        if (!string.IsNullOrEmpty(dlcList))
            info.DlcAppIds = dlcList.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        foreach (var dlcId in info.DlcAppIds)
            info.DlcDepots[dlcId] = [];

        var depotsNode = kv["depots"];
        foreach (var child in depotsNode.Children)
        {
            if (!uint.TryParse(child.Name, out var depotId))
                continue;

            if (depotId == appId)
                continue;

            if (child["manifests"] == KeyValue.Invalid && child["depotfromapp"] == KeyValue.Invalid)
                continue;

            var dlcAppId = child["dlcappid"].Value;

            if (!string.IsNullOrEmpty(dlcAppId) && info.DlcDepots.TryGetValue(dlcAppId, out var dlcDepotList))
                dlcDepotList.Add(depotId.ToString());
            else
                info.Depots.Add(depotId.ToString());
        }

        return info;
    }

    private async Task EnsureReadyAsync()
    {
        if (!_isConnected)
            await _connectedTcs.Task.ConfigureAwait(false);

        if (!_isLoggedOn)
            await _loggedOnTcs.Task.ConfigureAwait(false);
    }

    private async Task CallbackLoop()
    {
        while (_isRunning && !_cts.Token.IsCancellationRequested)
        {
            _callbackManager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    private void OnConnected(SteamClient.ConnectedCallback callback)
    {
        _isConnected = true;
        _connectedTcs.TrySetResult();
        _steamUser.LogOnAnonymous();
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        _isConnected = false;
        _isLoggedOn = false;

        Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
        {
            if (_isRunning) _steamClient.Connect();
        });
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result == EResult.OK)
        {
            _isLoggedOn = true;
            _loggedOnTcs.TrySetResult();
        }
    }

    private static string MapSteamTypeToDisplayType(string steamType)
    {
        return steamType.ToLower() switch
        {
            "game" => "Game",
            "dlc" => "DLC",
            "demo" => "Demo",
            "mod" => "Mod",
            "video" => "Video",
            "music" => "Soundtrack",
            "bundle" => "Bundle",
            "episode" => "Episode",
            "tool" or "advertising" => "Software",
            _ => "Game"
        };
    }
}

public record GameDetails(
    string AppId,
    string Type,
    string Name,
    string? ClientIconHash = null,
    string? HeroHash = null,
    string? MainHash = null,
    string? ParentAppId = null,
    string? HeaderImage = null
);