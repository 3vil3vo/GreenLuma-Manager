namespace GreenLuma_Manager.Services;

public static class InternetConnectivityChecker
{
    private const string NcsiUrl = "http://www.msftconnecttest.com/connecttest.txt";
    private const string NcsiExpectedBody = "Microsoft Connect Test";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

    private static readonly string[] FallbackProbeUrls =
    [
        "https://www.cloudflare.com/cdn-cgi/trace",
        "https://www.google.com/generate_204"
    ];

    public static async Task<bool> HasInternetConnectionAsync(CancellationToken ct = default)
    {
        if (await ProbeNcsiAsync(ct).ConfigureAwait(false))
            return true;

        foreach (var url in FallbackProbeUrls)
            if (await ProbeStatusOnlyAsync(url, ct).ConfigureAwait(false))
                return true;

        return false;
    }

    private static async Task<bool> ProbeNcsiAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            using var response = await HttpClientProvider.Default.GetAsync(NcsiUrl, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return string.Equals(body.Trim(), NcsiExpectedBody, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            Logger.Debug($"InternetConnectivityChecker: NCSI probe failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> ProbeStatusOnlyAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            using var response = await HttpClientProvider.Default.GetAsync(url, cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Debug($"InternetConnectivityChecker: probe failed for {url}: {ex.Message}");
            return false;
        }
    }
}