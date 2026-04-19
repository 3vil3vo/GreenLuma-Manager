using System.Net.Http;

namespace GreenLuma_Manager.Services;

public static class HttpClientProvider
{
    public static HttpClient Default { get; } = CreateDefault();
    public static HttpClient GitHub { get; } = CreateGitHub();

    private static HttpClient CreateDefault()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    private static HttpClient CreateGitHub()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "GreenLuma-Manager");
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }
}
