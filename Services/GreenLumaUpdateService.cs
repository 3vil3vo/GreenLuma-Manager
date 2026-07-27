using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using GreenLuma_Manager.Models;
using Microsoft.Web.WebView2.Wpf;

namespace GreenLuma_Manager.Services;

public static partial class GreenLumaUpdateService
{
    private const int PollIntervalMs = 1500;
    private const int FetchTimeoutMs = 30000;

    [GeneratedRegex(@"GreenLuma \d{4}\s+(\d+\.\d+\.\d+)")]
    private static partial Regex VersionTitleRegex();

    public static Version? LastKnownLatestVersion { get; private set; }

    public static async Task<GreenLumaVersionInfo?> CheckForGreenLumaUpdatesAsync(Config config)
    {
        if (!config.CheckGreenLumaUpdates)
            return null;

        return await RunCheckAsync(config).ConfigureAwait(false);
    }

    private const int MaxFailedAttemptsBeforeSettlingOff = 2;

    public static async Task<GreenLumaVersionInfo?> AutoDetectDefaultAsync(Config config)
    {
        if (config.GreenLumaUpdateCheckAutoDetectDone)
            return config.CheckGreenLumaUpdates ? await RunCheckAsync(config).ConfigureAwait(false) : null;

        if (!await InternetConnectivityChecker.HasInternetConnectionAsync().ConfigureAwait(false))
        {
            Logger.Debug("GreenLumaUpdateService.AutoDetectDefault: no internet connection, will retry next launch");
            return null;
        }

        var result = await RunCheckAsync(config).ConfigureAwait(false);

        if (result is { CheckSucceeded: true })
        {
            config.CheckGreenLumaUpdates = true;
            config.GreenLumaUpdateCheckAutoDetectDone = true;
            config.GreenLumaUpdateCheckFailedAttempts = 0;
        }
        else
        {
            config.GreenLumaUpdateCheckFailedAttempts++;
            if (config.GreenLumaUpdateCheckFailedAttempts >= MaxFailedAttemptsBeforeSettlingOff)
            {
                config.CheckGreenLumaUpdates = false;
                config.GreenLumaUpdateCheckAutoDetectDone = true;
            }
        }

        ConfigService.Save(config);

        Logger.Info(
            $"GreenLumaUpdateService.AutoDetectDefault: attempt result={result?.CheckSucceeded}, " +
            $"failedAttempts={config.GreenLumaUpdateCheckFailedAttempts}, done={config.GreenLumaUpdateCheckAutoDetectDone}, " +
            $"CheckGreenLumaUpdates={config.CheckGreenLumaUpdates}");

        return result;
    }

    private static async Task<GreenLumaVersionInfo?> RunCheckAsync(Config config)
    {
        try
        {
            var title = await FetchForumPageTitleAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(title))
                return CreateFailedResult("Failed to read the GreenLuma forum page.");

            var match = VersionTitleRegex().Match(title);
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var latestVersion))
                return CreateFailedResult($"Could not parse a version from the forum title: {title}");

            LastKnownLatestVersion = latestVersion;

            var detectedVersion = GreenLumaService.DetectVersion(config.GreenLumaPath);
            var effectiveVersion = GreenLumaService.ResolveVersion(config, detectedVersion);
            Version.TryParse(effectiveVersion, out var installedVersion);

            return new GreenLumaVersionInfo
            {
                LatestVersionTag = title,
                LatestSemanticVersion = latestVersion,
                InstalledVersion = installedVersion,
                CheckedAt = DateTime.UtcNow,
                CheckSucceeded = true
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GreenLumaUpdateService.CheckForUpdates");
            return CreateFailedResult(ex.Message);
        }
    }

    private static Task<string?> FetchForumPageTitleAsync()
    {
        var tcs = new TaskCompletionSource<string?>();

        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (Application.Current.MainWindow is not MainWindow mainWindow)
            {
                tcs.TrySetResult(null);
                return;
            }

            var hostPanel = mainWindow.PnlWebView2Host;
            var webView = new WebView2
            {
                Width = 1,
                Height = 1,
                Visibility = Visibility.Visible
            };

            hostPanel.Children.Add(webView);

            try
            {
                var environment = await WebView2Helper.GetEnvironmentAsync();
                await webView.EnsureCoreWebView2Async(environment);

                webView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

                webView.CoreWebView2.Navigate(GreenLumaVersionInfo.ForumUrl);

                var stopwatch = Stopwatch.StartNew();

                while (stopwatch.ElapsedMilliseconds < FetchTimeoutMs)
                {
                    await Task.Delay(PollIntervalMs);

                    try
                    {
                        var titleJson = await webView.CoreWebView2.ExecuteScriptAsync("document.title");
                        var title = titleJson?.Trim('"');

                        if (!string.IsNullOrWhiteSpace(title) && VersionTitleRegex().IsMatch(title))
                        {
                            tcs.TrySetResult(title);
                            return;
                        }
                    }
                    catch
                    {
                    }
                }

                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "GreenLumaUpdateService.FetchForumPageTitle");
                tcs.TrySetResult(null);
            }
            finally
            {
                hostPanel.Children.Remove(webView);
                webView.Dispose();
            }
        }, DispatcherPriority.Normal);

        return tcs.Task;
    }

    private static GreenLumaVersionInfo CreateFailedResult(string errorMessage)
    {
        return new GreenLumaVersionInfo
        {
            LatestVersionTag = string.Empty,
            CheckedAt = DateTime.UtcNow,
            CheckSucceeded = false,
            ErrorMessage = errorMessage
        };
    }
}
