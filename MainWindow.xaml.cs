using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    public const string Version = "RC2.12";

    // Controllers
    private readonly SearchController _searchController;
    private readonly ProfileController _profileController;
    private readonly GameListController _gameListController;
    private readonly AppListController _appListController;
    private readonly GreenLumaLauncher _launcher;
    private readonly NotificationManager _notificationManager;

    // UI state
    private readonly ObservableCollection<string> _profiles;
    private Config? _config;
    private CancellationTokenSource? _profileLoadCts;

    public MainWindow()
    {
        InitializeComponent();

        _profiles = [];
        CmbProfile.ItemsSource = _profiles;

        // Create notification manager (no deps)
        _notificationManager = new NotificationManager(
            Toast, ToastMessage, ToastIcon,
            StatusIndicator, TxtStatus, TxtGameCount,
            TxtLoadingDots);

        // Create launcher (no deps)
        _launcher = new GreenLumaLauncher();

        // Create game list controller (depends on NotificationManager)
        _gameListController = new GameListController(LstGames, TxtGameCount, PnlEmptyGames, _notificationManager);

        // Create search controller (depends on NotificationManager)
        _searchController = new SearchController(
            DgResults, PnlSearchLoading, TxtResultCount, PnlEmptyResults,
            TxtLoadingDots, _notificationManager);

        // Create profile controller (depends on GameListController, Launcher, NotificationManager)
        _profileController = new ProfileController(CmbProfile, _profiles, _gameListController, _launcher, _notificationManager);

        // Create app list controller (depends on ProfileController, GameListController, Launcher, NotificationManager)
        _appListController = new AppListController(_profileController, _gameListController, _launcher, _notificationManager);

        // Wire cross-controller events
        _searchController.GameSelected += OnSearchResultSelected;

        // Configure search result columns if needed
        ConfigureSearchResultColumns();

        // Commands
        FocusSearchCommand = new RelayCommand(_ => TxtSearchInput.Focus());
        GenerateApplistCommand = new RelayCommand(_ => GenerateApplistButton_Click(BtnGenerateApplist, new RoutedEventArgs()));
        LaunchGreenlumaCommand = new RelayCommand(_ => LaunchGreenlumaButton_Click(BtnLaunchGreenluma, new RoutedEventArgs()));
        ToggleStealthCommand = new RelayCommand(_ => TglStealthMode.IsChecked = !TglStealthMode.IsChecked.GetValueOrDefault());

        DataContext = this;

        // Startup initialization
        _config = ConfigService.Load();
        _profileController.Config = _config;
        _profileController.LoadProfileList();

        UpdatePluginButtons();
        CheckPathsOnStartup();
        CheckForUpdates();
        UpdateStatus();
        _gameListController.UpdateGameListState();
    }

    public ICommand FocusSearchCommand { get; }
    public ICommand GenerateApplistCommand { get; }
    public ICommand LaunchGreenlumaCommand { get; }
    public ICommand ToggleStealthCommand { get; }

    private void ConfigureSearchResultColumns()
    {
        // Columns are defined in XAML — no additional setup needed.
    }

    // ─── Search ───────────────────────────────────────────────────────

    private void SearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
            _ = _searchController.ExecuteSearchAsync(TxtSearchInput.Text.Trim());
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await _searchController.ExecuteSearchAsync(TxtSearchInput.Text.Trim());
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
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(animation);
        if (onComplete != null) storyboard.Completed += (_, _) => onComplete();
        storyboard.Begin();
    }

    private void SearchGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        TxtSearchInput.Focus();
    }

    // ─── Game List Search ───────────────────────────────────────────

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
        // Update placeholder visibility
        TxtGameSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtGameSearch.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Apply real-time filter
        _gameListController.SetSearchFilter(TxtGameSearch.Text);
    }

    private void CmbGameTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbGameTypeFilter.SelectedItem is ComboBoxItem item && item.Content is string type)
            _gameListController.SetTypeFilter(type);
    }

    private void SearchResult_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.DataContext is Game game)
            _searchController.OnSearchResultDoubleClick(game);
    }

    private void OnSearchResultSelected(Game game)
    {
        if (_profileController.CurrentProfile == null)
        {
            _notificationManager.ShowToast("No profile selected", false);
            return;
        }

        // Check if already in game list
        if (_gameListController.Games.Any(g => g.AppId == game.AppId))
        {
            _notificationManager.ShowToast($"{game.Name} is already in your profile", false);
            return;
        }

        _gameListController.AddGame(game);
        _profileController.CurrentProfile.Games.Add(game);
        _profileController.SaveCurrentProfile();

        _ = Task.Run(async () =>
        {
            try
            {
                var tempGame = new Game { AppId = game.AppId, Name = string.Empty, Type = game.Type };
                await SearchService.PopulateGameDetailsAsync(tempGame);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existingGame = _gameListController.Games.FirstOrDefault(g => g.AppId == game.AppId);
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
            catch
            {
                // ignored
            }
        });
    }

    // ─── Profile ──────────────────────────────────────────────────────

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbProfile.SelectedItem == null)
            return;

        var profileName = CmbProfile.SelectedItem.ToString();

        if (profileName == "__empty__")
        {
            RestorePreviousProfile(e);
            return;
        }

        if (profileName != null)
        {
            // Clear game search and type filter when switching profiles
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

    private void ProfileOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.ContextMenu == null)
            return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
        button.ContextMenu.Closed += (_, _) => button.IsChecked = false;
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
        _gameListController.ClearGames();
        _profileController.SaveCurrentProfile();
        _notificationManager.ShowToast($"Profile '{_profileController.CurrentProfile.Name}' cleared");
    }

    // ─── Game List ────────────────────────────────────────────────────

    private void CancelPendingProfileLoad()
    {
        if (_profileLoadCts != null)
        {
            _profileLoadCts.Cancel();

            var oldCts = _profileLoadCts;
            _ = Task.Run(async () =>
            {
                await Task.Delay(100, oldCts.Token);
                oldCts.Dispose();
            }, oldCts.Token);
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

            var gamesToProcess = _gameListController.Games.ToList();
            var semaphore = new SemaphoreSlim(6);

            var tasks = gamesToProcess.Select(async game =>
            {
                await semaphore.WaitAsync(token);
                try
                {
                    if (token.IsCancellationRequested) return;

                    if (!string.IsNullOrWhiteSpace(game.IconUrl))
                        return;

                    var tempGame = new Game { AppId = game.AppId, Name = string.Empty, Type = "Game" };
                    await SearchService.PopulateGameDetailsAsync(tempGame);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;

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
                catch
                {
                    // ignored
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }, token);
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
            _profileController.SaveCurrentProfile();
            _notificationManager.ShowToast("Game renamed");
        }
        else if (e.Key == Key.Escape)
        {
            _gameListController.CancelRename(game);
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

    private void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Game game })
            OnSearchResultSelected(game);
    }

    private void RemoveGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Game game)
            return;

        _gameListController.RemoveGame(game);
        _profileController.CurrentProfile?.Games.Remove(game);
        _profileController.SaveCurrentProfile();
        _notificationManager.ShowToast($"Removed '{game.Name}'");
    }

    // ─── Import AppList ───────────────────────────────────────────────

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

            var profileName = targetProfile.Name;
            var result = CustomMessageBox.Show(
                $"Found {importResult.AppIds.Count} items in existing AppList.\n\n" +
                $"Would you like to import them into '{profileName}' profile?",
                "Import AppList",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

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
        catch
        {
            HideAppListProgress();
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

    // ─── AppList Generation & Launch ──────────────────────────────────

    private async void GenerateApplistButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_config == null) return;

            if (!_launcher.ValidatePaths(_config))
            {
                _notificationManager.ShowToast("GreenLuma path not configured", false);
                return;
            }

            if (_profileController.CurrentProfile == null)
            {
                _notificationManager.ShowToast("No profile selected", false);
                return;
            }

            if (_gameListController.Games.Count == 0)
            {
                var clearResult = CustomMessageBox.Show(
                    "This profile contains no games. Clear the existing AppList?",
                    "Clear AppList",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (clearResult != MessageBoxResult.Yes)
                    return;
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
                }
                else
                {
                    _notificationManager.ShowToast("Failed to generate AppList", false);
                }
            }
            finally
            {
                BtnGenerateApplist.IsEnabled = true;
            }
        }
        catch
        {
            // ignored
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
                    "GreenLuma path is not configured. Open settings?",
                    "Launch GreenLuma",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    SettingsButton_Click(null, null!);

                return;
            }

            if (!_launcher.IsAppListGenerated(_config))
            {
                var generateResult = CustomMessageBox.Show(
                    "No AppList found. Generate one now?",
                    "Generate AppList",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (generateResult == MessageBoxResult.Yes)
                {
                    _profileController.SaveCurrentProfile();
                    await _appListController.GenerateAsync(_config, _profileController.CurrentProfile);
                }

                if (generateResult == MessageBoxResult.Cancel)
                    return;

                GenerateApplistButton_Click(BtnGenerateApplist, new RoutedEventArgs());
                await Task.Delay(500);
            }

            _profileController.SaveCurrentProfile();

            if (_launcher.ValidatePaths(_config) && await _launcher.LaunchAsync(_config))
                _notificationManager.ShowToast("GreenLuma launched successfully");
            else
                _notificationManager.ShowToast("Failed to launch GreenLuma", false);
        }
        catch
        {
            // ignored
        }
    }

    // ─── Settings & Status ────────────────────────────────────────────

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
                _profileController.Config = _config;
                UpdateStatus();

                var nowHasGreenLumaPath = !string.IsNullOrWhiteSpace(_config.GreenLumaPath);

                if (!hadGreenLumaPath && nowHasGreenLumaPath)
                    _ = ImportExistingAppListAfterSettings();
            }
        }
        catch
        {
            // ignored
        }
    }

    private async Task ImportExistingAppListAfterSettings()
    {
        var importResult = await _appListController.ImportExistingAppListAsync(_config!);
        if (!importResult.FoundAppList || importResult.AppIds.Count == 0) return;

        var targetProfile = _profileController.CurrentProfile
            ?? ProfileService.Load("default")
            ?? new Profile { Name = "default" };
        await _appListController.ResolveAndImportAppsAsync(importResult.AppIds, targetProfile);

        if (_profileController.CurrentProfile?.Name == targetProfile.Name)
            _profileController.LoadProfile(targetProfile.Name);
    }

    private void UpdateStatus()
    {
        if (_config == null)
        {
            _notificationManager.SetStatusIndicator(
                Resources["Danger"] as Brush ?? Brushes.Red, "Not Configured");
            return;
        }

        var steamPath = _config.SteamPath.Trim();
        var greenLumaPath = _config.GreenLumaPath.Trim();

        if (string.IsNullOrWhiteSpace(steamPath) || string.IsNullOrWhiteSpace(greenLumaPath))
        {
            _notificationManager.SetStatusIndicator(
                Resources["Danger"] as Brush ?? Brushes.Red, "Not Configured");
            return;
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

        if (isSamePath)
            _notificationManager.SetStatusIndicator(successBrush, "Ready  •  Normal Mode");
        else if (_config.NoHook)
            _notificationManager.SetStatusIndicator(successBrush,
                isStealthOnly ? "Ready  •  Stealth Mode (Forced)" : "Ready  •  Stealth Mode");
        else
            _notificationManager.SetStatusIndicator(successBrush, "Ready  •  Normal Mode");
    }

    private void NoHook_Toggled(object sender, RoutedEventArgs e)
    {
        if (_config == null || sender is not ToggleButton toggleButton)
            return;

        _config.NoHook = toggleButton.IsChecked.GetValueOrDefault();
        ConfigService.Save(_config);
        UpdateStatus();
    }

    // ─── Update ───────────────────────────────────────────────────────

    private async void CheckForUpdates()
    {
        try
        {
            if (_config?.DisableUpdateCheck == true)
                return;

            var updateInfo = await UpdateService.CheckForUpdatesAsync();
            if (updateInfo?.UpdateAvailable == true)
                await Dispatcher.InvokeAsync(() => HandleUpdateAvailable(updateInfo));
        }
        catch
        {
            // ignored
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
                if (await UpdateService.PerformAutoUpdateAsync(updateInfo.DownloadUrl))
                    Application.Current.Shutdown();
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

    // ─── Startup ──────────────────────────────────────────────────────

    private void CheckPathsOnStartup()
    {
        if (_config == null)
            return;

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

    // ─── Window Chrome ────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void GitHubButton_Click(object sender, RoutedEventArgs e) => LaunchBrowser("https://github.com/FroggMaster/GreenLuma-Manager");
    private static void LaunchBrowser(string url) => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    // ─── Plugins ──────────────────────────────────────────────────────

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
        catch
        {
            // ignored
        }
    }
}
