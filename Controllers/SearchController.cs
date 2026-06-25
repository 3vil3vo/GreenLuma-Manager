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
    private readonly Button _btnAddAll;
    private readonly DataGrid _dgResults;
    private readonly NotificationManager _notificationManager;
    private readonly UIElement _pnlEmptyResults;
    private readonly UIElement _pnlResultsHeader;
    private readonly UIElement _pnlSearchLoading;
    private CancellationTokenSource? _searchCts;

    public SearchController(
        DataGrid dgResults,
        UIElement pnlSearchLoading,
        UIElement pnlEmptyResults,
        UIElement pnlResultsHeader,
        Button btnAddAll,
        NotificationManager notificationManager)
    {
        _dgResults = dgResults;
        _pnlSearchLoading = pnlSearchLoading;
        _pnlEmptyResults = pnlEmptyResults;
        _pnlResultsHeader = pnlResultsHeader;
        _btnAddAll = btnAddAll;
        _notificationManager = notificationManager;

        SearchResults = [];
        _dgResults.ItemsSource = SearchResults;
    }

    public int TotalResultCount { get; private set; }

    public ObservableCollection<Game> SearchResults { get; }

    public event Action<Game>? GameSelected;
    public event Action? ResultsLoaded;

    public async Task ExecuteSearchAsync(string query, CancellationToken token = default)
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

        if (_searchCts != null)
        {
            await _searchCts.CancelAsync();
            _searchCts.Dispose();
        }

        _searchCts = new CancellationTokenSource();

        try
        {
            await PerformSearchAsync(query, _searchCts.Token);
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
        ShowLoading();

        var results = await Task.Run(() => SearchService.SearchAsync(query, ct: token), token);

        if (token.IsCancellationRequested) return;

        DisplayResults(results);

        if (results.Count == 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await SearchService.FetchIconUrlsAsync(results, () =>
                {
                    if (!token.IsCancellationRequested)
                        Application.Current.Dispatcher.Invoke(() => ResultsLoaded?.Invoke());
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("SearchController.FetchIcons", ex);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    Application.Current.Dispatcher.Invoke(() => ResultsLoaded?.Invoke());
            }
        }, token);
    }

    public void ShowLoading()
    {
        _dgResults.Visibility = Visibility.Collapsed;
        _pnlEmptyResults.Visibility = Visibility.Collapsed;
        _pnlSearchLoading.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _pnlSearchLoading.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        if (_pnlSearchLoading.RenderTransform is ScaleTransform transform)
        {
            var scaleIn = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
        }

        _notificationManager.StartLoadingDots();
    }

    public void HideLoading()
    {
        _notificationManager.StopLoadingDots();
        _pnlSearchLoading.Visibility = Visibility.Collapsed;
    }

    public void DisplayResults(List<Game> results)
    {
        TotalResultCount = results.Count;
        SearchResults.Clear();
        HideLoading();

        _pnlResultsHeader.Visibility = Visibility.Visible;

        if (results.Count == 0)
        {
            _dgResults.Visibility = Visibility.Collapsed;
            _pnlEmptyResults.Visibility = Visibility.Visible;
            _btnAddAll.Visibility = Visibility.Collapsed;
            ResultsLoaded?.Invoke();
            return;
        }

        foreach (var game in results.Take(100))
            SearchResults.Add(game);

        _dgResults.Visibility = Visibility.Visible;
        _pnlEmptyResults.Visibility = Visibility.Collapsed;
        _btnAddAll.Visibility = Visibility.Visible;
        ResultsLoaded?.Invoke();
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