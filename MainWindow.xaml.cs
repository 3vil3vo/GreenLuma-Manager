using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GreenLuma_Manager.Controllers;
using GreenLuma_Manager.Dialogs;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Plugins;
using GreenLuma_Manager.Services;
using GreenLuma_Manager.Utilities;

namespace GreenLuma_Manager;

public partial class MainWindow
{
    public const string Version = "RC2.16";
    private const string LatestGreenLumaVersion = "1.8.0";
    private readonly AppListController _appListController;
    private readonly GameListController _gameListController;
    private readonly GreenLumaLauncher _launcher;

    private readonly NotificationManager _notificationManager;
    private readonly ProfileController _profileController;
    private readonly ObservableCollection<string> _profiles;
    private readonly SearchController _searchController;
    private Config? _config;
    private List<Game>? _lastAddAllGames;
    private CancellationTokenSource? _profileLoadCts;

    public MainWindow()
    {
        InitializeComponent();

        _profiles = [];

        _notificationManager = new NotificationManager(
            Toast, ToastMessage, ToastIcon,
            StatusIndicator, TxtStatus, TxtGameCount, TxtLoadingDots);

        _launcher = new GreenLumaLauncher();

        _gameListController = new GameListController(LstGames, PnlEmptyGames, _notificationManager);

        _searchController = new SearchController(
            DgResults, PnlSearchLoading, PnlEmptyResults,
            PnlResultsHeader, BtnAddAll, _notificationManager);

        _profileController = new ProfileController(
            CmbProfile, _profiles, _gameListController, _notificationManager);

        _appListController = new AppListController(
            _profileController, _gameListController, _launcher, _notificationManager);

        _searchController.GameSelected += OnSearchResultSelected;
        _searchController.ResultsLoaded += UpdateResultCount;

        FocusSearchCommand = new RelayCommand(_ => TxtSearchInput.Focus());
        GenerateApplistCommand =
            new RelayCommand(_ => GenerateApplistButton_Click(BtnGenerateApplist, new RoutedEventArgs()));
        LaunchGreenlumaCommand =
            new RelayCommand(_ => LaunchGreenlumaButton_Click(BtnLaunchGreenluma, new RoutedEventArgs()));
        ToggleStealthCommand =
            new RelayCommand(_ => TglStealthMode.IsChecked = !TglStealthMode.IsChecked.GetValueOrDefault());
        CycleProfileCommand = new RelayCommand(_ => CycleProfile());

        DataContext = this;
        CmbProfile.ItemsSource = _profiles;

        _config = ConfigService.Load();
        if (_config != null)
        {
            TglStealthMode.IsChecked = _config.NoHook;
            SanitizeApiKey(_config);
            SearchService.SetApiKey(_config.SteamApiKey);
            if (_config.WindowWidth >= MinWidth && _config.WindowHeight >= MinHeight)
            {
                Width = _config.WindowWidth;
                Height = _config.WindowHeight;
            }
        }

        _profileController.Config = _config;
        _profileController.LoadProfileList();

        _gameListController.UpdateGameListState();
        UpdatePluginButtons();
        CheckPathsOnStartup();
        CheckApiKeyOnStartup();
        CheckForUpdates();
        UpdateStatus();
    }

    public ICommand FocusSearchCommand { get; }
    public ICommand GenerateApplistCommand { get; }
    public ICommand LaunchGreenlumaCommand { get; }
    public ICommand ToggleStealthCommand { get; }
    public ICommand CycleProfileCommand { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_config != null && WindowState == WindowState.Normal)
        {
            _config.WindowWidth = Width;
            _config.WindowHeight = Height;
            ConfigService.Save(_config);
        }

        base.OnClosing(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchBrowser("https://github.com/3vil3vo/GreenLuma-Manager");
    }

    private static void LaunchBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return;

        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private void SearchGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        TxtSearchInput.Focus();
    }

    private void SearchInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtSearchPlaceholder.Visibility != Visibility.Visible)
            return;
        AnimatePlaceholder(0.5, 0.0, () => TxtSearchPlaceholder.Visibility = Visibility.Collapsed);
    }

    private void SearchInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtSearchInput.Text))
            return;
        TxtSearchPlaceholder.Visibility = Visibility.Visible;
        TxtSearchPlaceholder.Opacity = 0.0;
        AnimatePlaceholder(0.0, 0.5);
    }

    private void AnimatePlaceholder(double from, double to, Action? onComplete = null)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(150));
        Storyboard.SetTarget(animation, TxtSearchPlaceholder);
        Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(animation);
        if (onComplete != null) storyboard.Completed += (_, _) => onComplete();
        storyboard.Begin();
    }

    private static void SanitizeApiKey(Config config)
    {
        if (config.SteamApiKey == "1DD0450A99F573693CD031EBB160907D")
        {
            config.SteamApiKey = string.Empty;
            ConfigService.Save(config);
        }
    }

    private void MaybeShowApiKeyNotice()
    {
        if (!string.IsNullOrWhiteSpace(_config?.SteamApiKey)) return;
        _notificationManager.ShowToast("No Steam API key set! Please add one in Settings.", false);
    }

    private void SearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return) return;
        var query = TxtSearchInput.Text.Trim();
        if (query.Length >= 3) MaybeShowApiKeyNotice();
        _ = _searchController.ExecuteSearchAsync(query);
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var query = TxtSearchInput.Text.Trim();

        if (uint.TryParse(query, out var appId) && appId > 0)
        {
            await TryAddByAppIdAsync(appId).ConfigureAwait(true);
            return;
        }

        if (query.Length >= 3) MaybeShowApiKeyNotice();
        await _searchController.ExecuteSearchAsync(query);
    }

    private void ResultFilter_Changed(object sender, RoutedEventArgs e)
    {
        UpdateResultCount();
        if (DgResults?.Items.Count > 0) DgResults.ScrollIntoView(DgResults.Items[0]!);
    }

    private void UpdateResultCount()
    {
        if (BtnHideAdded == null || TxtResultCount == null) return;
        var view = CollectionViewSource.GetDefaultView(_searchController.SearchResults);
        view.Filter = item =>
        {
            if (item is not Game g) return false;
            if (BtnHideAdded.IsChecked == true && g.IsInProfile) return false;
            if (FilterAll.IsChecked == true) return true;
            if (FilterGames.IsChecked == true) return string.Equals(g.Type, "Game", StringComparison.OrdinalIgnoreCase);
            if (FilterDlc.IsChecked == true) return string.Equals(g.Type, "DLC", StringComparison.OrdinalIgnoreCase);
            if (FilterOther.IsChecked == true)
                return !string.Equals(g.Type, "Game", StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(g.Type, "DLC", StringComparison.OrdinalIgnoreCase);
            return true;
        };
        var visible = view.Cast<object>().Count();
        TxtResultCount.Text = $"Showing {visible} of {_searchController.TotalResultCount} results";
    }

    private void SearchResult_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.DataContext is Game game)
            _searchController.OnSearchResultDoubleClick(game);
        else if (DgResults.SelectedItem is Game selectedGame)
            _searchController.OnSearchResultDoubleClick(selectedGame);
    }

    private void OnSearchResultSelected(Game game)
    {
        if (_profileController.CurrentProfile == null)
        {
            _notificationManager.ShowToast("No profile selected", false);
            return;
        }

        if (_gameListController.Games.Any(g => g.AppId == game.AppId))
        {
            _notificationManager.ShowToast($"{game.Name} is already in your profile", false);
            return;
        }

        var newGame = new Game
        {
            AppId = game.AppId,
            Name = game.Name,
            Type = game.Type,
            IconUrl = game.IconUrl
        };

        _gameListController.AddGame(newGame);
        _profileController.CurrentProfile.Games.Add(newGame);
        _profileController.SaveCurrentProfile();

        game.IsInProfile = true;
        UpdateResultCount();
        _notificationManager.ShowToast($"Added {game.Name}");

        _ = Task.Run(async () =>
        {
            try
            {
                var tempGame = new Game { AppId = newGame.AppId, Name = string.Empty, Type = newGame.Type };
                await SearchService.PopulateGameDetailsAsync(tempGame).ConfigureAwait(false);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existingGame = _gameListController.Games.FirstOrDefault(g => g.AppId == newGame.AppId);
                    if (existingGame == null) return;

                    if (!string.IsNullOrEmpty(tempGame.Name))
                        existingGame.Name = tempGame.Name;

                    existingGame.Type = tempGame.Type;

                    if (!string.IsNullOrEmpty(tempGame.IconUrl))
                    {
                        existingGame.IconUrl = tempGame.IconUrl;
                        _profileController.SaveCurrentProfile();
                    }
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("MainWindow.OnSearchResultSelected.Background", ex);
            }
        });
    }

    private void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Game game })
            OnSearchResultSelected(game);
    }

    private void AddAllGames_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (_lastAddAllGames != null)
        {
            var removed = 0;
            foreach (var game in _lastAddAllGames)
            {
                var existing = _gameListController.Games.FirstOrDefault(g => g.AppId == game.AppId);
                if (existing != null)
                {
                    _gameListController.RemoveGame(existing);
                    var searchResult = _searchController.SearchResults.FirstOrDefault(g => g.AppId == game.AppId);
                    if (searchResult != null) searchResult.IsInProfile = false;
                    removed++;
                }
            }

            if (removed > 0)
            {
                _profileController.SaveCurrentProfile();
                UpdateResultCount();
                _notificationManager.ShowToast($"Removed {removed} game{(removed == 1 ? "" : "s")}");
            }

            _lastAddAllGames = null;
            btn.Content = "ADD ALL";
            return;
        }

        if (_searchController.SearchResults.Count == 0) return;

        var added = new List<Game>();
        foreach (var result in _searchController.SearchResults.ToList())
        {
            if (_gameListController.Games.Any(g => g.AppId == result.AppId))
                continue;

            OnSearchResultSelected(result);
            added.Add(result);
        }

        if (added.Count > 0)
        {
            _lastAddAllGames = added;
            btn.Content = "UNDO";
        }
        else
        {
            _notificationManager.ShowToast("All results already in profile", false);
        }
    }

    private async Task TryAddByAppIdAsync(uint appId)
    {
        var appIdStr = appId.ToString();

        try
        {
            _searchController.ShowLoading();

            var details = await Task.Run(() => SteamService.Instance.GetGameDetailsAsync(appId)).ConfigureAwait(true);

            if (details != null && details.Name != $"App {appId}")
            {
                _searchController.HideLoading();

                var game = new Game
                {
                    AppId = appIdStr,
                    Name = details.Name,
                    Type = details.Type,
                    IconUrl = string.Empty
                };

                OnSearchResultSelected(game);
                return;
            }

            var pkgAppIds = await Task.Run(() => SteamService.Instance.GetPackageAppIdsAsync(appId))
                .ConfigureAwait(true);

            if (pkgAppIds.Count > 0)
            {
                var appDetails = await Task.Run(() => SteamService.Instance.GetAppInfoBatchAsync(pkgAppIds))
                    .ConfigureAwait(true);

                var results = new List<Game>();
                var unresolvedIds = new List<string>();

                foreach (var pkgAppId in pkgAppIds)
                {
                    var pkgAppIdStr = pkgAppId.ToString();
                    appDetails.TryGetValue(pkgAppId, out var d);
                    var name = d?.Name ?? $"App {pkgAppId}";
                    var type = d?.Type ?? "DLC";

                    if (name == $"App {pkgAppId}")
                        unresolvedIds.Add(pkgAppIdStr);

                    results.Add(new Game { AppId = pkgAppIdStr, Name = name, Type = type, IconUrl = string.Empty });
                }

                if (unresolvedIds.Count > 0)
                {
                    var resolved = await SearchService.ResolveAppNamesAsync(unresolvedIds).ConfigureAwait(true);
                    foreach (var game in results)
                        if (resolved.TryGetValue(game.AppId, out var resolvedName))
                            game.Name = resolvedName;
                }

                _searchController.HideLoading();

                if (!results.Exists(g => g.AppId == appIdStr))
                    results.Insert(0, new Game
                    {
                        AppId = appIdStr,
                        Name = details != null && details.Name != $"App {appId}" ? details.Name : $"App {appId}",
                        Type = details?.Type ?? "Package",
                        IconUrl = string.Empty
                    });

                _searchController.DisplayResults(results);
                _notificationManager.ShowToast($"Found {results.Count} apps in package {appId}");
                return;
            }

            _searchController.HideLoading();

            var result = CustomMessageBox.Show(
                $"App ID {appId} was not found on Steam.\n\nDo you want to add it anyway as an unrecognized app?",
                "App Not Found",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                OnSearchResultSelected(new Game
                {
                    AppId = appIdStr,
                    Name = $"Unknown App {appId}",
                    Type = "Unknown",
                    IconUrl = string.Empty
                });
        }
        catch (Exception ex)
        {
            _searchController.HideLoading();
            _notificationManager.ShowToast("Failed to look up ID: " + ex.Message, false);
            LogService.LogError("MainWindow.TryAddByAppId", ex);
        }
    }

    private void TxtGameSearch_GotFocus(object sender, RoutedEventArgs e)
    {
        TxtGameSearchPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void TxtGameSearch_LostFocus(object sender, RoutedEventArgs e)
    {
        TxtGameSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtGameSearch.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TxtGameSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtGameSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtGameSearch.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _gameListController.SetSearchFilter(TxtGameSearch.Text);
    }

    private void CmbGameTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbGameTypeFilter.SelectedItem is ComboBoxItem item && item.Content is string type)
            _gameListController.SetTypeFilter(type);
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbProfile.SelectedItem == null) return;

        var profileName = CmbProfile.SelectedItem.ToString();

        if (profileName == "__empty__")
        {
            RestorePreviousProfile(e);
            return;
        }

        if (profileName != null)
        {
            TxtGameSearch.Text = string.Empty;
            if (CmbGameTypeFilter.SelectedIndex != 0)
                CmbGameTypeFilter.SelectedIndex = 0;

            CancelPendingProfileLoad();
            _profileController.SelectProfile(profileName);
            ScheduleGameDetailLoad();
        }
    }

    private void RestorePreviousProfile(SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is string removedItem && removedItem != "__empty__")
            CmbProfile.SelectedItem = removedItem;
        else
            foreach (var profile in _profiles)
                if (profile != "__empty__")
                {
                    CmbProfile.SelectedItem = profile;
                    break;
                }
    }

    private void CancelPendingProfileLoad()
    {
        if (_profileLoadCts != null)
        {
            _profileLoadCts.Cancel();
            _profileLoadCts.Dispose();
        }

        _profileLoadCts = new CancellationTokenSource();
    }

    private void ScheduleGameDetailLoad()
    {
        if (_profileLoadCts == null) return;
        var token = _profileLoadCts.Token;

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, token);
            if (token.IsCancellationRequested) return;

            var gamesToProcess = _gameListController.Games
                .Where(g => string.IsNullOrWhiteSpace(g.IconUrl))
                .ToList();

            await Parallel.ForEachAsync(
                gamesToProcess,
                new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = token },
                async (game, ct) =>
                {
                    try
                    {
                        var tempGame = new Game { AppId = game.AppId, Name = string.Empty, Type = "Game" };
                        await SearchService.PopulateGameDetailsAsync(tempGame).ConfigureAwait(false);

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (ct.IsCancellationRequested) return;

                            if (!string.IsNullOrEmpty(tempGame.Name))
                                game.Name = tempGame.Name;

                            game.Type = tempGame.Type;

                            if (!string.IsNullOrEmpty(tempGame.IconUrl))
                            {
                                game.IconUrl = tempGame.IconUrl;
                                _profileController.SaveCurrentProfile();
                            }
                        }, DispatcherPriority.Background);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        LogService.LogError("MainWindow.ProfileLoadIcon", ex);
                    }
                });
        }, token);
    }

    private void ProfileOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.ContextMenu == null)
            return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
        button.ContextMenu.Closed += (_, _) => button.IsChecked = false;
    }

    private void CreateProfileButton_Click(object sender, RoutedEventArgs e)
    {
        _profileController.CreateProfile();
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        _profileController.DeleteProfile(CmbProfile.SelectedItem?.ToString());
    }

    private void ImportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        _profileController.ImportProfile();
    }

    private void ExportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        _profileController.ExportProfile();
    }

    private void ClearProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profileController.CurrentProfile == null)
        {
            _notificationManager.ShowToast("No profile selected", false);
            return;
        }

        if (_gameListController.Games.Count == 0)
        {
            _notificationManager.ShowToast("Profile is already empty");
            return;
        }

        var result = CustomMessageBox.Show(
            $"Remove all {_gameListController.Games.Count} game(s) from '{_profileController.CurrentProfile.Name}'?",
            "Clear Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Exclamation);

        if (result != MessageBoxResult.Yes) return;

        TxtGameSearch.Text = string.Empty;
        foreach (var game in _gameListController.Games.ToList())
            IconCacheService.DeleteCachedIcon(game.AppId);

        _gameListController.ClearGames();
        _profileController.SaveCurrentProfile();
        _notificationManager.ShowToast($"Profile '{_profileController.CurrentProfile.Name}' cleared");
    }

    private void GameName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not TextBlock textBlock || textBlock.DataContext is not Game game)
            return;

        _gameListController.StartRename(game);
        e.Handled = true;
    }

    private void GameNameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.Visibility == Visibility.Visible)
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                textBox.Focus();
                textBox.SelectAll();
            }));
    }

    private void GameNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not Game game)
            return;

        if (e.Key == Key.Return)
        {
            _gameListController.CommitRename(game, textBox.Text);
            if (!game.IsEditing)
            {
                _profileController.SaveCurrentProfile();
                _notificationManager.ShowToast("Game renamed");
            }

            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _gameListController.CancelRename(game);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void GameNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not Game game)
            return;

        if (!game.IsEditing) return;
        _gameListController.CommitRename(game, textBox.Text);
        _profileController.SaveCurrentProfile();
    }

    private void RemoveGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Game game)
            return;

        IconCacheService.DeleteCachedIcon(game.AppId);
        _gameListController.RemoveGame(game);

        var searchResult = _searchController.SearchResults.FirstOrDefault(g => g.AppId == game.AppId);
        if (searchResult != null)
        {
            searchResult.IconUrl = string.Empty;
            searchResult.IsInProfile = false;
            UpdateResultCount();
        }

        _profileController.CurrentProfile?.Games.Remove(game);
        _profileController.SaveCurrentProfile();
        _notificationManager.ShowToast("Game removed from profile");
    }

    private void MoveGameUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu ctx ||
            ctx.PlacementTarget is not FrameworkElement fe ||
            fe.DataContext is not Game game)
            return;

        _gameListController.MoveUp(game);
        _profileController.SaveCurrentProfile();
    }

    private void MoveGameDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu ctx ||
            ctx.PlacementTarget is not FrameworkElement fe ||
            fe.DataContext is not Game game)
            return;

        _gameListController.MoveDown(game);
        _profileController.SaveCurrentProfile();
    }

    private async void LoadAppListButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_config == null) return;

            var importResult = await _appListController.ImportExistingAppListAsync(_config);

            if (!importResult.FoundAppList)
            {
                _notificationManager.ShowToast("No existing AppList found", false);
                return;
            }

            var targetProfile = _profileController.CurrentProfile
                                ?? ProfileService.Load("default")
                                ?? new Profile { Name = "default" };

            var result = CustomMessageBox.Show(
                $"Found {importResult.AppIds.Count} items in existing AppList.\n\n" +
                $"Would you like to import them into '{targetProfile.Name}' profile?",
                "Import AppList",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var progress = new Progress<AppListProgressReport>(report =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtAppListProgress.Text = report.Status;
                    AppListProgressBar.IsIndeterminate = report.IsIndeterminate;
                    if (!report.IsIndeterminate && report.Total > 0)
                        AppListProgressBar.Value = report.Percentage;
                });
            });

            ShowAppListProgress();

            try
            {
                await _appListController.ResolveAndImportAppsAsync(importResult.AppIds, targetProfile, progress);

                if (_profileController.CurrentProfile?.Name == targetProfile.Name)
                    _profileController.LoadProfile(targetProfile.Name);

                if (importResult.HasSteamWarning)
                    CustomMessageBox.Show(
                        "WARNING: AppList was found in your Steam folder.\n\n" +
                        "For better stealth, you should uninstall GreenLuma from the Steam folder " +
                        "and use it from a separate location instead.",
                        "Stealth Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
            }
            finally
            {
                HideAppListProgress();
            }
        }
        catch (Exception ex)
        {
            HideAppListProgress();
            LogService.LogError("MainWindow.LoadAppList", ex);
        }
    }

    private void ShowAppListProgress()
    {
        TxtAppListProgress.Text = "Starting...";
        AppListProgressBar.IsIndeterminate = true;
        AppListProgressBar.Value = 0;
        PnlAppListProgress.Visibility = Visibility.Visible;
    }

    private void HideAppListProgress()
    {
        PnlAppListProgress.Visibility = Visibility.Collapsed;
    }

    private async void GenerateApplistButton_Click(object sender, RoutedEventArgs e)
    {
        await GenerateAppListWithChecksAsync();
    }

    private async Task<bool> GenerateAppListWithChecksAsync()
    {
        try
        {
            if (_config == null) return false;

            if (!_launcher.ValidatePaths(_config))
            {
                var result = CustomMessageBox.Show(
                    "Steam and GreenLuma paths must be configured first.\n\nOpen settings now?",
                    "Paths Not Set",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation);

                if (result == MessageBoxResult.Yes) SettingsButton_Click(null, null!);
                return false;
            }

            if (_profileController.CurrentProfile == null)
            {
                _notificationManager.ShowToast("No profile selected", false);
                return false;
            }

            if (_gameListController.Games.Count == 0)
            {
                var clearResult = CustomMessageBox.Show(
                    "This profile contains no games. Clear the existing AppList?",
                    "Clear AppList",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (clearResult != MessageBoxResult.Yes)
                    return false;
            }

            BtnGenerateApplist.IsEnabled = false;

            try
            {
                _profileController.SaveCurrentProfile();

                var totalAppIds = await _appListController.GenerateAsync(_config, _profileController.CurrentProfile);

                if (totalAppIds >= 0)
                {
                    var generatedCount = Math.Min(totalAppIds, GreenLumaService.AppListLimit);

                    if (generatedCount > 0)
                    {
                        var itemWord = generatedCount == 1 ? "item" : "items";
                        _notificationManager.ShowToast($"Generated AppList with {generatedCount} {itemWord}");
                    }
                    else
                    {
                        _notificationManager.ShowToast("AppList cleared successfully");
                    }

                    if (totalAppIds > GreenLumaService.AppListLimit)
                    {
                        var droppedCount = totalAppIds - GreenLumaService.AppListLimit;
                        CustomMessageBox.Show(
                            $"Warning: Your profile lists {totalAppIds} item(s), but GreenLuma is limited to {GreenLumaService.AppListLimit} entries.\n\n" +
                            $"{droppedCount} item(s) were excluded from the generated AppList.\n\n" +
                            "Consider creating a smaller profile for the games you intend to launch.",
                            "AppList Truncated",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }

                    return true;
                }

                _notificationManager.ShowToast("Failed to generate AppList - check paths in settings", false);
                return false;
            }
            catch (Exception ex)
            {
                _notificationManager.ShowToast("Error: " + ex.Message, false);
                return false;
            }
            finally
            {
                BtnGenerateApplist.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.GenerateApplist", ex);
            return false;
        }
    }

    private async void LaunchGreenlumaButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_config == null) return;

            if (!_launcher.ValidatePaths(_config))
            {
                var result = CustomMessageBox.Show(
                    "Steam and GreenLuma paths must be configured first.\n\nOpen settings now?",
                    "Paths Not Set",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation);

                if (result == MessageBoxResult.Yes) SettingsButton_Click(null, null!);
                return;
            }

            if (!await CheckAndGenerateAppList().ConfigureAwait(true))
                return;

            var issues = GreenLumaService.RunPreLaunchDiagnostics(_config!);
            if (issues.Count > 0)
            {
                var issueText = string.Join("\n• ", issues);
                var diagResult = CustomMessageBox.Show(
                    $"Pre-launch check found potential issues:\n\n• {issueText}\n\nLaunch anyway?",
                    "Diagnostic Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation);

                if (diagResult != MessageBoxResult.Yes)
                    return;
            }

            var confirm = CustomMessageBox.Show(
                "This will close Steam and launch GreenLuma. Continue?",
                "Launch GreenLuma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            BtnLaunchGreenluma.IsEnabled = false;

            try
            {
                _profileController.SaveCurrentProfile();

                if (await _launcher.LaunchAsync(_config).ConfigureAwait(true))
                {
                    _notificationManager.ShowToast("GreenLuma launched. Monitoring Steam...");

                    var crashInfo = await GreenLumaService.MonitorSteamAfterLaunchAsync(_config).ConfigureAwait(true);
                    if (crashInfo != null)
                        CustomMessageBox.Show(
                            $"Steam crashed after injection:\n\n{crashInfo}",
                            "Steam Crash Detected",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    else
                        _notificationManager.ShowToast("GreenLuma launched successfully");
                }
                else
                {
                    _notificationManager.ShowToast("Failed to launch GreenLuma. Check settings.", false);
                }
            }
            catch (Exception ex)
            {
                _notificationManager.ShowToast("Error: " + ex.Message, false);
            }
            finally
            {
                BtnLaunchGreenluma.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.LaunchGreenluma", ex);
        }
    }

    private async Task<bool> CheckAndGenerateAppList()
    {
        if (_config != null && GreenLumaService.IsAppListGenerated(_config))
            return true;

        var result = CustomMessageBox.Show(
            "AppList has not been generated.\n\nGenerate AppList now, or continue without it?",
            "AppList Not Generated",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Cancel => false,
            MessageBoxResult.Yes => await GenerateAppListWithChecksAsync(),
            _ => true
        };
    }

    private void SettingsButton_Click(object? sender, RoutedEventArgs? e)
    {
        try
        {
            if (_config == null) return;

            var hadGreenLumaPath = !string.IsNullOrWhiteSpace(_config.GreenLumaPath);
            var dialog = new SettingsDialog(_config);

            if (dialog.ShowDialog() == true)
            {
                _config = ConfigService.Load();
                SanitizeApiKey(_config);
                SearchService.SetApiKey(_config.SteamApiKey);
                _profileController.Config = _config;
                UpdateStatus();

                var nowHasGreenLumaPath = !string.IsNullOrWhiteSpace(_config.GreenLumaPath);

                if (!hadGreenLumaPath && nowHasGreenLumaPath)
                    _ = ImportExistingAppListAfterSettings();
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.SettingsButton", ex);
        }
    }

    private async Task ImportExistingAppListAfterSettings()
    {
        if (_config == null) return;

        var importResult = await _appListController.ImportExistingAppListAsync(_config);
        if (!importResult.FoundAppList || importResult.AppIds.Count == 0) return;

        var targetProfile = _profileController.CurrentProfile
                            ?? ProfileService.Load("default")
                            ?? new Profile { Name = "default" };

        ShowAppListProgress();
        try
        {
            await _appListController.ResolveAndImportAppsAsync(importResult.AppIds, targetProfile);

            if (_profileController.CurrentProfile?.Name == targetProfile.Name)
                _profileController.LoadProfile(targetProfile.Name);

            if (importResult.HasSteamWarning)
                CustomMessageBox.Show(
                    "WARNING: AppList was found in your Steam folder.\n\n" +
                    "For better stealth, you should uninstall GreenLuma from the Steam folder " +
                    "and use it from a separate location instead.",
                    "Stealth Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
        }
        finally
        {
            HideAppListProgress();
        }
    }

    private void NoHook_Toggled(object sender, RoutedEventArgs e)
    {
        if (_config == null || sender is not ToggleButton toggleButton)
            return;

        _config.NoHook = toggleButton.IsChecked.GetValueOrDefault();
        ConfigService.Save(_config);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_config == null)
        {
            TxtGreenLumaVersionStatus.Visibility = Visibility.Collapsed;
            TxtVersionDot.Visibility = Visibility.Collapsed;
            _notificationManager.SetStatusIndicator(Resources["Danger"] as Brush ?? Brushes.Red, "Not Configured");
            return;
        }

        var steamPath = _config.SteamPath.Trim();
        var greenLumaPath = _config.GreenLumaPath.Trim();

        if (string.IsNullOrWhiteSpace(steamPath) || string.IsNullOrWhiteSpace(greenLumaPath))
        {
            TxtGreenLumaVersionStatus.Visibility = Visibility.Collapsed;
            TxtVersionDot.Visibility = Visibility.Collapsed;
            _notificationManager.SetStatusIndicator(Resources["Danger"] as Brush ?? Brushes.Red, "Not Configured");
            return;
        }

        var glVersion = GreenLumaService.DetectVersion(greenLumaPath);
        if (glVersion != null && !_config.DisableGreenLumaVersionNotice)
        {
            var isOutdated = IsGreenLumaOutdated(glVersion);
            TxtGreenLumaVersionStatus.Text = isOutdated ? $"GL v{glVersion} (outdated)" : $"GL v{glVersion}";
            TxtGreenLumaVersionStatus.Foreground = isOutdated
                ? Resources["Warning"] as Brush ?? Brushes.Orange
                : Resources["TextTert"] as Brush ?? Brushes.Gray;
            TxtGreenLumaVersionStatus.Visibility = Visibility.Visible;
            TxtVersionDot.Visibility = Visibility.Visible;
        }
        else
        {
            TxtGreenLumaVersionStatus.Visibility = Visibility.Collapsed;
            TxtVersionDot.Visibility = Visibility.Collapsed;
        }

        var (isValid, isStealthOnly, _) = GreenLumaService.ValidateInstallation(greenLumaPath);

        if (isValid && isStealthOnly)
        {
            TglStealthMode.IsChecked = true;
            TglStealthMode.IsEnabled = false;

            if (!_config.NoHook)
            {
                _config.NoHook = true;
                ConfigService.Save(_config);
            }
        }
        else
        {
            TglStealthMode.IsEnabled = true;
        }

        var isSamePath = string.Equals(
            Path.GetFullPath(steamPath),
            Path.GetFullPath(greenLumaPath),
            StringComparison.OrdinalIgnoreCase);

        var successBrush = Resources["Success"] as Brush ?? Brushes.Green;

        if (isValid && isStealthOnly)
            _notificationManager.SetStatusIndicator(successBrush, "Ready  •  Stealth Mode (Forced)");
        else if (isSamePath)
            _notificationManager.SetStatusIndicator(successBrush, "Ready  •  Normal Mode");
        else if (_config.NoHook)
            _notificationManager.SetStatusIndicator(successBrush, "Ready  •  Stealth Mode");
        else
            _notificationManager.SetStatusIndicator(successBrush, "Ready  •  Normal Mode");
    }

    private static bool IsGreenLumaOutdated(string detectedVersion)
    {
        return GreenLumaService.CompareVersions(detectedVersion, LatestGreenLumaVersion) < 0;
    }

    private async void CheckForUpdates()
    {
        try
        {
            if (_config?.DisableUpdateCheck == true) return;

            var updateInfo = await UpdateService.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updateInfo?.UpdateAvailable == true)
                await Application.Current.Dispatcher.InvokeAsync(() => HandleUpdateAvailable(updateInfo));
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.CheckForUpdates", ex);
        }
    }

    private async Task HandleUpdateAvailable(UpdateInfo updateInfo)
    {
        if (_config?.AutoUpdate == true && !string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
        {
            var result = CustomMessageBox.Show(
                $"Current Version: {updateInfo.CurrentVersion}\nLatest Version: {updateInfo.LatestVersion}\n\n" +
                "Auto-update is enabled. The update will be downloaded and installed automatically.\n\n" +
                "The application will restart to complete the update.",
                "Update Available",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Asterisk);

            if (result == MessageBoxResult.OK)
            {
                if (await UpdateService.PerformAutoUpdateAsync(updateInfo.DownloadUrl).ConfigureAwait(false))
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    _notificationManager.ShowToast("Auto-update failed. Please download manually.", false);
                    LaunchBrowser(updateInfo.DownloadUrl);
                }
            }
        }
        else
        {
            var result = CustomMessageBox.Show(
                $"Current Version: {updateInfo.CurrentVersion}\nLatest Version: {updateInfo.LatestVersion}\n\n" +
                "Would you like to download the update now?",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Asterisk);

            if (result == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
                LaunchBrowser(updateInfo.DownloadUrl);
        }
    }

    private void CheckPathsOnStartup()
    {
        if (_config == null) return;

        if (!_config.FirstRun ||
            (!string.IsNullOrWhiteSpace(_config.SteamPath) && !string.IsNullOrWhiteSpace(_config.GreenLumaPath)))
            return;

        _config.FirstRun = false;
        ConfigService.Save(_config);

        Dispatcher.BeginInvoke((Action)(() =>
        {
            var result = CustomMessageBox.Show(
                "Steam and GreenLuma paths could not be detected automatically.\n\n" +
                "Please configure them in Settings to use all features.",
                "Setup Required",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Asterisk);

            if (result == MessageBoxResult.OK) SettingsButton_Click(null, null!);
        }), DispatcherPriority.Loaded);
    }

    private void CheckApiKeyOnStartup()
    {
        if (_config == null || !string.IsNullOrWhiteSpace(_config.SteamApiKey)) return;

        Dispatcher.BeginInvoke((Action)(() =>
        {
            var result = CustomMessageBox.Show(
                "No Steam API key is configured.\n\n" +
                "A key is required for full search coverage. Add one in Settings.",
                "Steam API Key Missing",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Asterisk);

            if (result == MessageBoxResult.OK) SettingsButton_Click(null, null!);
        }), DispatcherPriority.Loaded);
    }

    private void ManagePluginsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PluginsDialog { Owner = this };
        dialog.ShowDialog();
        UpdatePluginButtons();
    }

    public void UpdatePluginButtons()
    {
        PnlPluginButtons.Children.Clear();

        var plugins = PluginService.GetEnabledPlugins();

        foreach (var plugin in plugins)
        {
            var button = new Button
            {
                Style = (Style)FindResource("IconBtn"),
                ToolTip = plugin.Name,
                Margin = new Thickness(0, 0, 8, 0),
                Tag = plugin
            };

            var path = new System.Windows.Shapes.Path
            {
                Width = 18,
                Height = 18,
                Data = plugin.Icon,
                Fill = (SolidColorBrush)FindResource("TextSecond"),
                Stretch = Stretch.Uniform
            };

            button.Content = path;
            button.Click += PluginButton_Click;
            PnlPluginButtons.Children.Add(button);
        }
    }

    private void PluginButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not IPlugin plugin) return;

        try
        {
            plugin.ShowUi(this);
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.PluginButton", ex);
        }
    }

    private void CycleProfile()
    {
        var realProfiles = _profiles.Where(p => p != "__empty__").ToList();
        if (realProfiles.Count <= 1) return;

        var currentIndex = realProfiles.IndexOf(CmbProfile.SelectedItem?.ToString() ?? string.Empty);
        var nextIndex = (currentIndex + 1) % realProfiles.Count;
        CmbProfile.SelectedItem = realProfiles[nextIndex];
    }
}