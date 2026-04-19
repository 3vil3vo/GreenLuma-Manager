using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace GreenLuma_Manager.Services;

public class IconCacheService
{
    private static readonly string IconCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GLM_Manager",
        "icons");

    private static readonly HttpClient Client = new();

    static IconCacheService()
    {
        Client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        Client.Timeout = TimeSpan.FromSeconds(10);
    }

    public static async Task<string?> DownloadAndCacheIconAsync(string appId, string iconUrl)
    {
        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(iconUrl))
            return null;
        if (!iconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            EnsureIconCacheDirectoryExists();
            var extension = GetImageExtension(iconUrl);
            var filePath = Path.Combine(IconCacheDir, appId + extension);

            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                return filePath;

            var data = await TryDownloadWithRetries(iconUrl, 3, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (data is { Length: > 0 })
            {
                await File.WriteAllBytesAsync(filePath, data).ConfigureAwait(false);
                return filePath;
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("IconCacheService.DownloadAndCache", ex);
        }

        return null;
    }

    public static async Task<string?> CacheIconForGameAsync(GameDetails details)
    {
        return await CacheIconForGameRecursiveAsync(details, 0).ConfigureAwait(false);
    }

    private static async Task<string?> CacheIconForGameRecursiveAsync(GameDetails details, int depth)
    {
        if (depth > 2) return null;

        EnsureIconCacheDirectoryExists();

        var isDlc = string.Equals(details.Type, "DLC", StringComparison.OrdinalIgnoreCase);
        var cached = GetCachedIconPath(details.AppId, isDlc);
        if (!string.IsNullOrEmpty(cached)) return cached;

        var candidates = new List<IconCandidate>();

        if (isDlc)
        {
            if (!string.IsNullOrEmpty(details.HeroHash))
            {
                candidates.Add(new IconCandidate(
                    $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/{details.HeroHash}/hero_capsule.jpg"));
                candidates.Add(new IconCandidate(
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/{details.HeroHash}/hero_capsule.jpg"));
            }

            if (!string.IsNullOrEmpty(details.MainHash))
            {
                candidates.Add(new IconCandidate(
                    $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/{details.MainHash}/capsule_616x353.jpg"));
                candidates.Add(new IconCandidate(
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/{details.MainHash}/capsule_616x353.jpg"));
            }
            else
            {
                candidates.Add(new IconCandidate(
                    $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/capsule_616x353.jpg"));
                candidates.Add(new IconCandidate(
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/capsule_616x353.jpg"));
            }

            if (!string.IsNullOrEmpty(details.HeaderImage))
            {
                candidates.Add(new IconCandidate(
                    $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/{details.HeaderImage}"));
                candidates.Add(new IconCandidate(
                    $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/{details.HeaderImage}"));
                candidates.Add(new IconCandidate(
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/{details.HeaderImage}"));
            }

            candidates.Add(new IconCandidate(
                $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/header.jpg"));
            candidates.Add(new IconCandidate(
                $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/header.jpg"));
            candidates.Add(new IconCandidate(
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/header.jpg"));
        }
        else
        {
            if (!string.IsNullOrEmpty(details.ClientIconHash))
            {
                candidates.Add(new IconCandidate(
                    $"https://shared.fastly.steamstatic.com/community_assets/images/apps/{details.AppId}/{details.ClientIconHash}.ico",
                    64));
                candidates.Add(new IconCandidate(
                    $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{details.AppId}/{details.ClientIconHash}.ico",
                    64));
            }

            if (!string.IsNullOrEmpty(details.HeroHash))
            {
                candidates.Add(new IconCandidate(
                    $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/{details.HeroHash}/hero_capsule.jpg"));
                candidates.Add(new IconCandidate(
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/{details.HeroHash}/hero_capsule.jpg"));
            }

            if (!string.IsNullOrEmpty(details.MainHash))
            {
                candidates.Add(new IconCandidate(
                    $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/{details.MainHash}/capsule_616x353.jpg"));
                candidates.Add(new IconCandidate(
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/{details.MainHash}/capsule_616x353.jpg"));
            }
            else
            {
                candidates.Add(new IconCandidate(
                    $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/capsule_616x353.jpg"));
                candidates.Add(new IconCandidate(
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/capsule_616x353.jpg"));
            }

            if (!string.IsNullOrEmpty(details.HeaderImage))
            {
                candidates.Add(new IconCandidate(
                    $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/{details.HeaderImage}"));
                candidates.Add(new IconCandidate(
                    $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/{details.HeaderImage}"));
            }

            candidates.Add(new IconCandidate(
                $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{details.AppId}/header.jpg"));
            candidates.Add(new IconCandidate(
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{details.AppId}/header.jpg"));
        }

        foreach (var candidate in candidates)
            try
            {
                var data = await TryDownloadWithRetries(candidate.Url, 2, TimeSpan.FromMilliseconds(500))
                    .ConfigureAwait(false);
                if (data == null || data.Length == 0) continue;

                if (candidate.MinSize > 0 && !IsValidImageSize(data, candidate.MinSize))
                    continue;

                var extension = GetImageExtension(candidate.Url);
                var filePath = Path.Combine(IconCacheDir, details.AppId + extension);
                await File.WriteAllBytesAsync(filePath, data).ConfigureAwait(false);
                return filePath;
            }
            catch (Exception ex)
            {
                LogService.LogError("IconCacheService.CacheCandidate", ex);
            }

        if (isDlc && !string.IsNullOrEmpty(details.ParentAppId) && uint.TryParse(details.ParentAppId, out var parentId))
            try
            {
                var parentDetails = await SteamService.Instance.GetGameDetailsAsync(parentId).ConfigureAwait(false);
                if (parentDetails != null)
                {
                    var parentPath = await CacheIconForGameRecursiveAsync(parentDetails, depth + 1)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(parentPath) && File.Exists(parentPath))
                    {
                        var ext = Path.GetExtension(parentPath);
                        var myPath = Path.Combine(IconCacheDir, details.AppId + ext);
                        File.Copy(parentPath, myPath, true);
                        return myPath;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("IconCacheService.CacheParent", ex);
            }

        return null;
    }

    private static bool IsValidImageSize(byte[] data, int minSize)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return false;
            var frame = decoder.Frames[0];
            return frame.PixelWidth >= minSize;
        }
        catch (Exception ex)
        {
            LogService.LogError("IconCacheService.IsValidImageSize", ex);
            return false;
        }
    }

    private static async Task<byte[]?> TryDownloadWithRetries(string url, int maxAttempts, TimeSpan delay)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                using var resp = await Client.GetAsync(url, cts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return null;
                var data = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                if (data.Length > 0)
                    return data;
            }
            catch (Exception ex)
            {
                LogService.LogError("IconCacheService.TryDownload", ex);
                if (attempt == maxAttempts)
                    break;
            }

            await Task.Delay(delay).ConfigureAwait(false);
        }

        return null;
    }

    public static string? GetCachedIconPath(string appId, bool preferJpg = false)
    {
        if (string.IsNullOrEmpty(appId)) return null;

        try
        {
            if (!Directory.Exists(IconCacheDir)) return null;

            var preferences = preferJpg
                ? new[] { ".jpg", ".jpeg", ".ico", ".png", ".webp" }
                : new[] { ".ico", ".jpg", ".jpeg", ".png", ".webp" };

            foreach (var ext in preferences)
            {
                var filePath = Path.Combine(IconCacheDir, $"{appId}{ext}");
                if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                    return filePath;
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("IconCacheService.GetCachedIconPath", ex);
        }

        return null;
    }

    public static void DeleteCachedIcon(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return;

        try
        {
            if (!Directory.Exists(IconCacheDir)) return;

            string[] extensions = [".jpg", ".ico", ".jpeg", ".png", ".gif", ".webp"];

            foreach (var ext in extensions)
            {
                var filePath = Path.Combine(IconCacheDir, $"{appId}{ext}");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("IconCacheService.DeleteCachedIcon", ex);
        }
    }

    public static void DeleteUnusedIcons(HashSet<string> validAppIds)
    {
        try
        {
            if (!Directory.Exists(IconCacheDir)) return;
            var files = Directory.GetFiles(IconCacheDir);
            foreach (var file in files)
                try
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!validAppIds.Contains(name))
                        File.Delete(file);
                }
                catch (Exception ex)
                {
                    LogService.LogError("IconCacheService.DeleteUnusedIcon", ex);
                }
        }
        catch (Exception ex)
        {
            LogService.LogError("IconCacheService.DeleteUnusedIcons", ex);
        }
    }

    private static void EnsureIconCacheDirectoryExists()
    {
        if (!Directory.Exists(IconCacheDir)) Directory.CreateDirectory(IconCacheDir);
    }

    private static string GetImageExtension(string url)
    {
        try
        {
            var lower = url.ToLower();
            if (lower.Contains(".ico")) return ".ico";
            if (lower.Contains(".jpg") || lower.Contains("jpeg")) return ".jpg";
            if (lower.Contains(".png")) return ".png";
            if (lower.Contains(".gif")) return ".gif";
            if (lower.Contains(".webp")) return ".webp";
            return ".jpg";
        }
        catch (Exception ex)
        {
            LogService.LogError("IconCacheService.GetImageExtension", ex);
            return ".jpg";
        }
    }

    private record IconCandidate(string Url, int MinSize = 0);
}