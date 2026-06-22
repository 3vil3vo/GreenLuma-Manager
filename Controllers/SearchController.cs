using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;

namespace GreenLuma_Manager.Controllers;

public class SearchController
{
    private readonly DataGrid _dgResults;
    private readonly UIElement _pnlSearchLoading;
    private readonly TextBlock _txtResultCount;
    private readonly UIElement _pnlEmptyResults;
    private readonly TextBlock _txtLoadingDots;
    private readonly NotificationManager _notificationManager;

    private readonly ObservableCollection<Game> _searchResults;
    private CancellationTokenSource? _searchCts;

    public event Action<Game>? GameSelected;

    public ObservableCollection<Game> SearchResults => _searchResults;

    public SearchController(
        DataGrid dgResults,
        UIElement pnlSearchLoading,
        TextBlock txtResultCount,
        UIElement pnlEmptyResults,
        TextBlock txtLoadingDots,
        NotificationManager notificationManager,
        ObservableCollection<Game>? searchResults = null)
    {
        _dgResults = dgResults;
        _pnlSearchLoading = pnlSearchLoading;
        _txtResultCount = txtResultCount;
        _pnlEmptyResults = pnlEmptyResults;
        _txtLoadingDots = txtLoadingDots;
        _notificationManager = notificationManager;

        _searchResults = searchResults ?? [];
        _dgResults.ItemsSource = _searchResults;
    }

    public async Task ExecuteSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _notificationManager.ShowToast("Enter a search term", false);
            return;
        }

        if (query.Length < 3)
        {
            _notificationManager.ShowToast("Search term must be at least 3 characters", false);
            return;
        }

        if (query.Length > 200)
        {
            _notificationManager.ShowToast("Search term is too long (max 200 characters)", false);
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            await PerformSearchAsync(query, token);
        }
        catch (OperationCanceledException)
        {
            _notificationManager.StopLoadingDots();
        }
        catch (Exception ex)
        {
            _notificationManager.StopLoadingDots();
            _notificationManager.ShowToast("Search failed: " + ex.Message, false);
        }
    }

    private async Task PerformSearchAsync(string query, CancellationToken token)
    {
        Keyboard.ClearFocus();
        ShowSearchLoading();

        var results = await Task.Run(() => SearchService.SearchAsync(query), token);

        if (token.IsCancellationRequested)
            return;

        DisplaySearchResults(results, token);
    }

    private void ShowSearchLoading()
    {
        _dgResults.Visibility = Visibility.Collapsed;
        _pnlEmptyResults.Visibility = Visibility.Collapsed;
        _pnlSearchLoading.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var scaleIn = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _pnlSearchLoading.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        if (_pnlSearchLoading.RenderTransform is ScaleTransform scaleTransform)
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
        }

        _notificationManager.StartLoadingDots();
    }

    private void HideSearchLoading()
    {
        _notificationManager.StopLoadingDots();
        _pnlSearchLoading.Visibility = Visibility.Collapsed;
    }

    private void DisplaySearchResults(List<Game> results, CancellationToken token)
    {
        HideSearchLoading();

        _searchResults.Clear();

        if (results.Count == 0)
        {
            _dgResults.Visibility = Visibility.Collapsed;
            _pnlEmptyResults.Visibility = Visibility.Visible;
            return;
        }

        foreach (var game in results.Take(60))
            _searchResults.Add(game);

        _dgResults.Visibility = Visibility.Visible;
        _txtResultCount.Text = results.Count > 60
            ? $"Showing 60 of {results.Count} results"
            : $"{results.Count} results";

        _ = Task.Run(async () =>
        {
            try
            {
                await SearchService.FetchIconUrlsAsync(results);
            }
            catch
            {
                // ignored
            }
        }, token);
    }

    public void CancelSearch()
    {
        _searchCts?.Cancel();
    }

    public void OnSearchResultDoubleClick(Game game)
    {
        GameSelected?.Invoke(game);
    }
}
