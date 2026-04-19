namespace GreenLuma_Manager.Services;

public class AppPackageInfo
{
    public string AppId { get; set; } = string.Empty;
    public List<string> Depots { get; set; } = [];
    public List<string> DlcAppIds { get; set; } = [];
    public Dictionary<string, List<string>> DlcDepots { get; set; } = [];
}

public static class DepotService
{
    public static async Task<AppPackageInfo?> FetchAppPackageInfoAsync(string appId)
    {
        if (!uint.TryParse(appId, out var id))
            return null;

        return await SteamService.Instance.GetAppPackageInfoAsync(id).ConfigureAwait(false);
    }
}