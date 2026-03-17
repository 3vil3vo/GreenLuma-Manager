using SteamKit2;

namespace GreenLuma_Manager.Services;

public sealed class SteamService : IDisposable
{
    private static readonly Lazy<SteamService> InstanceHolder = new(() => new SteamService());

    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(30);

    private readonly Task _callbackLoop;
    private readonly CallbackManager _callbackManager;
    private readonly CancellationTokenSource _cts;
    private readonly SteamApps _steamApps;
    private readonly SteamClient _steamClient;
    private readonly SteamUser _steamUser;
    private readonly object _readyLock = new();
    private readonly SemaphoreSlim _apiThrottle = new(2, 2);

    private TaskCompletionSource _connectedTcs;
    private TaskCompletionSource _loggedOnTcs;
    private volatile bool _isConnected;
    private volatile bool _isLoggedOn;
    private volatile bool _isRunning;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private int _reconnectAttempt;

    private SteamService()
    {
        _steamClient = new SteamClient(SteamConfiguration.Create(b => b
            .WithProtocolTypes(ProtocolTypes.WebSocket)
            .WithConnectionTimeout(TimeSpan.FromSeconds(10))
        ));
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
        catch
        {
            // ignored
        }

        _cts.Dispose();
        _apiThrottle.Dispose();
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

        await _apiThrottle.WaitAsync().ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt <= maxRetries; attempt++)
                try
                {
                    if (!await EnsureReadyAsync().ConfigureAwait(false))
                        return results;

                    var requests = appIds.Select(id => new SteamApps.PICSRequest { ID = id, AccessToken = 0 }).ToList();

                    var job = _steamApps.PICSGetProductInfo(requests, []);
                    var task = job.ToTask();

                    if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))) != task)
                    {
                        ObserveTask(task);
                        _lastFailureTime = DateTime.UtcNow;
                        break;
                    }

                    var result = await task.ConfigureAwait(false);

                    if (result.Failed || result.Results == null)
                    {
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(500).ConfigureAwait(false);
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
                catch
                {
                    if (attempt == maxRetries) break;
                    await Task.Delay(500).ConfigureAwait(false);
                }
        }
        finally
        {
            _apiThrottle.Release();
        }

        return results;
    }

    public async Task<AppPackageInfo?> GetAppPackageInfoAsync(uint appId)
    {
        await _apiThrottle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!await EnsureReadyAsync().ConfigureAwait(false))
                return null;

            var request = new SteamApps.PICSRequest { ID = appId, AccessToken = 0 };
            var job = _steamApps.PICSGetProductInfo([request], []);
            var task = job.ToTask();

            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))) != task)
            {
                ObserveTask(task);
                _lastFailureTime = DateTime.UtcNow;
                return null;
            }

            var result = await task.ConfigureAwait(false);

            if (result.Failed || result.Results == null)
                return null;

            foreach (var callback in result.Results)
                if (callback.Apps.TryGetValue(appId, out var appData))
                    return ParseAppPackageInfo(appId, appData);

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            _apiThrottle.Release();
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

    private async Task<bool> EnsureReadyAsync()
    {
        if (_isConnected && _isLoggedOn)
            return true;

        if (DateTime.UtcNow - _lastFailureTime < FailureCooldown)
            return false;

        TaskCompletionSource connectedTcs;
        TaskCompletionSource loggedOnTcs;
        lock (_readyLock)
        {
            connectedTcs = _connectedTcs;
            loggedOnTcs = _loggedOnTcs;
        }

        if (!_isConnected)
        {
            var completed = await Task.WhenAny(connectedTcs.Task, Task.Delay(ConnectionTimeout)).ConfigureAwait(false);
            if (completed != connectedTcs.Task)
            {
                _lastFailureTime = DateTime.UtcNow;
                return false;
            }
        }

        if (!_isLoggedOn)
        {
            var completed = await Task.WhenAny(loggedOnTcs.Task, Task.Delay(ConnectionTimeout)).ConfigureAwait(false);
            if (completed != loggedOnTcs.Task)
            {
                _lastFailureTime = DateTime.UtcNow;
                return false;
            }
        }

        return true;
    }

    private async Task CallbackLoop()
    {
        while (_isRunning && !_cts.Token.IsCancellationRequested)
        {
            _callbackManager.RunCallbacks();
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    private void OnConnected(SteamClient.ConnectedCallback callback)
    {
        _reconnectAttempt = 0;
        lock (_readyLock)
        {
            _isConnected = true;
            _connectedTcs.TrySetResult();
        }

        _steamUser.LogOnAnonymous();
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        lock (_readyLock)
        {
            _isConnected = false;
            _isLoggedOn = false;
            _connectedTcs = new TaskCompletionSource();
            _loggedOnTcs = new TaskCompletionSource();
        }

        if (!_isRunning || callback.UserInitiated)
            return;

        var delay = Math.Min(5 * (1 << Math.Min(_reconnectAttempt, 4)), 60);
        _reconnectAttempt++;

        Task.Delay(TimeSpan.FromSeconds(delay)).ContinueWith(_ =>
        {
            if (_isRunning) _steamClient.Connect();
        });
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result == EResult.OK)
        {
            lock (_readyLock)
            {
                _isLoggedOn = true;
                _loggedOnTcs.TrySetResult();
            }

            _lastFailureTime = DateTime.MinValue;
        }
    }

    private static void ObserveTask(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        _ = task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.NotOnRanToCompletion);
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