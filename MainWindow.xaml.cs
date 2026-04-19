using System.Collections.Concurrent;
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
using GreenLuma_Manager.Dialogs;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Plugins;
using GreenLuma_Manager.Services;
using GreenLuma_Manager.Utilities;
using Microsoft.Win32;

namespace GreenLuma_Manager;

public partial class MainWindow
{
    public const string Version = "RC2.12";
    private const int ToastDurationMs = 6000;

    private readonly ObservableCollection<Game> _games;
    private readonly ObservableCollection<string> _profiles;
    private readonly ObservableCollection<Game> _searchResults;

    private Config? _config;
    private Profile? _currentProfile;
    private string? _editingOriginalName;
    private List<Game>? _lastAddAllGames;
    private DispatcherTimer? _loadingDotsTimer;
    private CancellationTokenSource? _profileLoadCts;
    private CancellationTokenSource? _searchCts;

    public MainWindow()
    {
        InitializeComponent();
        UpdatePluginButtons();

        _searchResults = [];
        _games = [];
        _profiles = [];

        FocusSearchCommand = new RelayCommand(_ => TxtSearchInput.Focus());
        GenerateApplistCommand =
            new RelayCommand(_ => GenerateApplistButton_Click(BtnGenerateApplist, new RoutedEventArgs()));
        LaunchGreenlumaCommand =
            new RelayCommand(_ => LaunchGreenlumaButton_Click(BtnLaunchGreenluma, new RoutedEventArgs()));
        ToggleStealthCommand =
            new RelayCommand(_ => TglStealthMode.IsChecked = !TglStealthMode.IsChecked.GetValueOrDefault());
        CycleProfileCommand = new RelayCommand(_ => CycleProfile());

        DataContext = this;
        DgResults.ItemsSource = _searchResults;
        LstGames.ItemsSource = _games;
        CmbProfile.ItemsSource = _profiles;

        InitializeLoadingTimer();
        LoadConfig();
        LoadProfiles();
        CheckForUpdates();
        CheckPathsOnStartup();
        UpdateGameListState();
        UpdateStatusIndicator();
    }

    public ICommand FocusSearchCommand { get; }
    public ICommand GenerateApplistCommand { get; }
    public ICommand LaunchGreenlumaCommand { get; }
    public ICommand ToggleStealthCommand { get; }
    public ICommand CycleProfileCommand { get; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_config != null && WindowState == WindowState.Normal)
        {
            _config.WindowWidth = Width;
            _config.WindowHeight = Height;
            ConfigService.Save(_config);
        }
        base.OnClosing(e);
    }

    private void InitializeLoadingTimer()
    {
        _loadingDotsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _loadingDotsTimer.Tick += LoadingDotsTimer_Tick;
    }

    private void LoadingDotsTimer_Tick(object? sender, EventArgs e)
    {
        var text = TxtLoadingDots.Text;
        TxtLoadingDots.Text = text.Length >= 3 ? "." : text + ".";
    }

    private void LoadConfig()
    {
        _config = ConfigService.Load();
        if (_config != null)
        {
            TglStealthMode.IsChecked = _config.NoHook;
            SearchService.SetApiKey(_config.SteamApiKey);

            if (_config.WindowWidth >= MinWidth && _config.WindowHeight >= MinHeight)
            {
                Width = _config.WindowWidth;
                Height = _config.WindowHeight;
            }
        }
        UpdateStatusIndicator();
    }

    private void LoadProfiles()
    {
        _profiles.Clear();
        foreach (var profile in ProfileService.LoadAll())
            if (profile.Name != "__empty__")
                _profiles.Add(profile.Name);

        if (_profiles.Count == 0) _profiles.Add("default");

        var lastProfile = _config?.LastProfile ?? "default";

        if (_profiles.Contains(lastProfile))
            CmbProfile.SelectedItem = lastProfile;
        else
            CmbProfile.SelectedIndex = 0;

        if (_profiles.Count == 1) _profiles.Add("__empty__");
    }

    private async void CheckForUpdates()
    {
        try
        {
            if (_config?.DisableUpdateCheck == true)
                return;

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
                    ShowToast("Auto-update failed. Please download manually.", false);
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchBrowser("https://github.com/3vil3vo/GreenLuma-Manager");
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs? e)
    {
        try
        {
            if (_config == null)
                return;

            var hadGreenLumaPath = !string.IsNullOrWhiteSpace(_config.GreenLumaPath);

            var dialog = new SettingsDialog(_config);

            if (dialog.ShowDialog() == true)
            {
                LoadConfig();
                UpdateStatusIndicator();

                var nowHasGreenLumaPath = !string.IsNullOrWhiteSpace(_config.GreenLumaPath);

                if (!hadGreenLumaPath && nowHasGreenLumaPath)
                    await ImportExistingAppListAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.SettingsButton", ex);
        }
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

    private void SearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return)
            return;

        Keyboard.ClearFocus();
        e.Handled = true;
        Dispatcher.BeginInvoke((Action)(() => BtnSearch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))));
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var query = TxtSearchInput.Text.Trim();

            if (!ValidateSearchQuery(query))
                return;

            if (_searchCts != null)
                await _searchCts.CancelAsync();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            try
            {
                await PerformSearch(query, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                _loadingDotsTimer?.Stop();
            }
            catch (Exception ex)
            {
                _loadingDotsTimer?.Stop();
                ShowToast("Search failed: " + ex.Message, false);
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.SearchButton", ex);
        }
    }

    private bool ValidateSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowToast("Enter a search term", false);
            return false;
        }

        if (query.Length < 3)
        {
            ShowToast("Search term must be at least 3 characters", false);
            return false;
        }

        if (query.Length > 200)
        {
            ShowToast("Search term is too long (max 200 characters)", false);
            return false;
        }

        return true;
    }

    private async Task PerformSearch(string query, CancellationToken token)
    {
        Keyboard.ClearFocus();

        ShowSearchLoading();

        var results = await Task.Run(() => SearchService.SearchAsync(query, ct: token), token).ConfigureAwait(true);

        if (token.IsCancellationRequested)
            return;

        await SearchService.FetchIconUrlsAsync(results, token).ConfigureAwait(true);

        if (token.IsCancellationRequested)
            return;

        DisplaySearchResults(results);
    }

    private void ShowSearchLoading()
    {
        DgResults.Visibility = Visibility.Collapsed;
        PnlEmptyResults.Visibility = Visibility.Collapsed;
        PnlSearchLoading.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var scaleIn = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        PnlSearchLoading.BeginAnimation(OpacityProperty, fadeIn);

        var transform = (ScaleTransform)PnlSearchLoading.RenderTransform;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);

        TxtLoadingDots.Text = ".";
        _loadingDotsTimer?.Start();
    }

    private void DisplaySearchResults(List<Game> results)
    {
        _searchResults.Clear();
        _lastAddAllGames = null;
        if (BtnAddAll != null) BtnAddAll.Content = "ADD ALL";

        var existingIds = new HashSet<string>(_games.Select(g => g.AppId));
        foreach (var game in results)
        {
            game.IsInProfile = existingIds.Contains(game.AppId);
            _searchResults.Add(game);
        }

        DgResults.Items.SortDescriptions.Clear();

        foreach (var column in DgResults.Columns)
        {
            column.SortDirection = null;
            column.CanUserSort = true;
        }

        TxtResultCount.Text = _searchResults.Count.ToString();
        PnlResultsHeader.Visibility = Visibility.Visible;
        _loadingDotsTimer?.Stop();

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += async (_, _) =>
        {
            PnlSearchLoading.Visibility = Visibility.Collapsed;
            await Task.Delay(50);

            if (_searchResults.Count > 0)
                ShowResultsGrid();
            else
                ShowEmptyResults();
        };

        PnlSearchLoading.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ShowResultsGrid()
    {
        DgResults.Opacity = 0.0;
        DgResults.Visibility = Visibility.Visible;
        PnlEmptyResults.Visibility = Visibility.Collapsed;

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        DgResults.BeginAnimation(OpacityProperty, fadeIn);
        SetAddAllVisibility(Visibility.Visible);
        ShowToast($"Found {_searchResults.Count} results");
    }

    private void ShowEmptyResults()
    {
        DgResults.Visibility = Visibility.Collapsed;
        PnlEmptyResults.Visibility = Visibility.Visible;
        SetAddAllVisibility(Visibility.Collapsed);
        ShowToast("No results found", false);
    }

    private void SetAddAllVisibility(Visibility visibility)
    {
        if (BtnAddAll != null)
            BtnAddAll.Visibility = visibility;
    }

    private void ResultRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DgResults.SelectedItem is Game selectedGame)
        {
            var fakeButton = new Button { Tag = selectedGame };
            AddGameButton_Click(fakeButton, new RoutedEventArgs());
        }
    }

    private void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Game result)
            return;

        AddGameToProfile(result);
    }

    private void AddAllGames_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (_lastAddAllGames != null)
        {
            var removed = 0;
            foreach (var game in _lastAddAllGames)
            {
                var existing = _games.FirstOrDefault(g => g.AppId == game.AppId);
                if (existing != null)
                {
                    _games.Remove(existing);
                    removed++;
                }
            }

            if (removed > 0)
            {
                SaveCurrentProfile();
                UpdateGameListState();
                ShowToast($"Removed {removed} game{(removed == 1 ? "" : "s")}");
            }

            _lastAddAllGames = null;
            btn.Content = "ADD ALL";
            return;
        }

        if (_searchResults.Count == 0) return;

        var added = new List<Game>();
        foreach (var result in _searchResults.ToList())
        {
            if (_games.Any(g => g.AppId == result.AppId))
                continue;

            AddGameToProfile(result, showToast: false);
            added.Add(result);
        }

        if (added.Count > 0)
        {
            _lastAddAllGames = added;
            btn.Content = "UNDO";
            ShowToast($"Added {added.Count} game{(added.Count == 1 ? "" : "s")}");
        }
        else
        {
            ShowToast("All results already in profile", false);
        }
    }

    private void AddGameToProfile(Game result, bool showToast = true)
    {
        if (_games.Any(g => g.AppId == result.AppId))
        {
            if (showToast) ShowToast(result.Name + " already in profile", false);
            return;
        }

        var newGame = new Game
        {
            AppId = result.AppId,
            Name = result.Name,
            Type = result.Type,
            IconUrl = result.IconUrl
        };

        _games.Add(newGame);
        result.IsInProfile = true;
        ShowToast("Added " + result.Name);
        SaveCurrentProfile();
        UpdateGameListState();

        var gameId = newGame.AppId;
        var gameName = newGame.Name;
        var gameType = newGame.Type;

        _ = Task.Run(async () =>
        {
            try
            {
                var tempGame = new Game { AppId = gameId, Name = gameName, Type = gameType };
                await SearchService.PopulateGameDetailsAsync(tempGame).ConfigureAwait(false);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (newGame.Name != tempGame.Name && !string.IsNullOrEmpty(tempGame.Name))
                        newGame.Name = tempGame.Name;

                    newGame.Type = tempGame.Type;

                    if (!string.IsNullOrEmpty(tempGame.IconUrl))
                    {
                        newGame.IconUrl = tempGame.IconUrl;
                        SaveCurrentProfile();
                    }
                });
            }
            catch (Exception ex)
            {
                LogService.LogError("MainWindow.AddGameBackground", ex);
            }
        });
    }

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

        if (profileName != null) LoadProfile(profileName);
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

    private void LoadProfile(string profileName)
    {
        try
        {
            _profileLoadCts?.Cancel();
            _profileLoadCts?.Dispose();

            _profileLoadCts = new CancellationTokenSource();
            var token = _profileLoadCts.Token;

            _currentProfile = ProfileService.Load(profileName);

            if (_currentProfile == null)
            {
                _currentProfile = new Profile { Name = profileName };
                ProfileService.Save(_currentProfile);
            }

            _games.Clear();
            foreach (var game in _currentProfile.Games) _games.Add(game);

            if (_config != null)
            {
                _config.LastProfile = profileName;
                ConfigService.Save(_config);
            }

            UpdateGameListState();

            var gamesToProcess = _games.ToList();
            _ = UpdateIconsForCurrentProfileAsync(gamesToProcess, token);
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.LoadProfile", ex);
        }
    }

    private async Task UpdateIconsForCurrentProfileAsync(List<Game> games, CancellationToken token)
    {
        var semaphore = new SemaphoreSlim(6);
        var tasks = new List<Task>();
        var changed = false;

        foreach (var game in games)
        {
            if (token.IsCancellationRequested) break;

            await semaphore.WaitAsync(token).ConfigureAwait(true);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;

                    var isDlc = string.Equals(game.Type, "DLC", StringComparison.OrdinalIgnoreCase);
                    var cachedPath = IconCacheService.GetCachedIconPath(game.AppId, !isDlc);

                    if (string.IsNullOrEmpty(cachedPath) ||
                        game.IconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        var tempGame = new Game { AppId = game.AppId, Name = game.Name, Type = game.Type };
                        await SearchService.PopulateGameDetailsAsync(tempGame).ConfigureAwait(false);

                        if (!string.IsNullOrEmpty(tempGame.IconUrl) && tempGame.IconUrl != game.IconUrl)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                game.IconUrl = tempGame.IconUrl;
                                if (string.IsNullOrEmpty(game.Name) || game.Name.StartsWith("App "))
                                    game.Name = tempGame.Name;
                                game.Type = tempGame.Type;
                            });
                            changed = true;
                        }
                    }
                    else if (game.IconUrl != cachedPath)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => game.IconUrl = cachedPath);
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError("MainWindow.UpdateIcons", ex);
                }
                finally
                {
                    semaphore.Release();
                }
            }, token));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);

        if (changed && !token.IsCancellationRequested)
            await Application.Current.Dispatcher.InvokeAsync(SaveCurrentProfile);
    }

    private void SaveCurrentProfile()
    {
        if (_currentProfile == null)
            return;

        _currentProfile.Games.Clear();

        foreach (var game in _games) _currentProfile.Games.Add(game);

        ProfileService.Save(_currentProfile);
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

    private void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateProfileDialog();

        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        var newProfile = dialog.Result;

        if (_profiles.Any(p => p.Equals(newProfile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            CustomMessageBox.Show("A profile with this name already exists.", "Duplicate Profile",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profiles.Remove("__empty__");

        ProfileService.Save(newProfile);
        _profiles.Add(newProfile.Name);
        CmbProfile.SelectedItem = newProfile.Name;

        ShowToast($"Created profile '{newProfile.Name}'");
    }

    private void ImportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files|*.json|All files|*.*",
                Title = "Import Profile"
            };

            if (dialog.ShowDialog() != true)
                return;

            var profile = ProfileService.Import(dialog.FileName);

            if (profile == null)
            {
                ShowToast("Failed to import profile", false);
                return;
            }

            foreach (var game in profile.Games) game.IconUrl = string.Empty;

            if (!ValidateProfileName(profile.Name))
                return;

            if (_profiles.Contains(profile.Name))
            {
                var result = CustomMessageBox.Show(
                    $"Profile '{profile.Name}' already exists. Overwrite?",
                    "Import Profile",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }
            else
            {
                _profiles.Remove("__empty__");
                _profiles.Add(profile.Name);
            }

            ProfileService.Save(profile);

            if (CmbProfile.SelectedItem?.ToString() == profile.Name)
                LoadProfile(profile.Name);
            else
                CmbProfile.SelectedItem = profile.Name;

            ShowToast($"Imported profile '{profile.Name}'");
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.ImportProfile", ex);
        }
    }

    private bool ValidateProfileName(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) || profileName.Length > 50)
        {
            ShowToast("Invalid profile name in imported file", false);
            return false;
        }

        if (profileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            profileName.Contains('/') || profileName.Contains('\\'))
        {
            ShowToast("Profile name contains invalid characters", false);
            return false;
        }

        return true;
    }

    private void ExportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProfile == null)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files|*.json|All files|*.*",
            FileName = _currentProfile.Name + ".json",
            Title = "Export Profile"
        };

        if (dialog.ShowDialog() != true)
            return;

        ProfileService.Export(_currentProfile, dialog.FileName);
        ShowToast($"Exported '{_currentProfile.Name}'");
    }

    private async void LoadAppListButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_config == null)
            {
                ShowToast("Configure paths in Settings first", false);
                return;
            }

            var greenLumaAppListPath = !string.IsNullOrWhiteSpace(_config.GreenLumaPath)
                ? Path.Combine(_config.GreenLumaPath, "AppList")
                : null;

            if (greenLumaAppListPath == null || !Directory.Exists(greenLumaAppListPath) ||
                Directory.GetFiles(greenLumaAppListPath, "*.txt").Length == 0)
            {
                ShowToast("No AppList found in the GreenLuma folder.", false);
                return;
            }

            var appIds = new HashSet<string>();
            try
            {
                foreach (var file in Directory.GetFiles(greenLumaAppListPath, "*.txt"))
                {
                    var id = (await File.ReadAllTextAsync(file)).Trim();
                    if (!string.IsNullOrWhiteSpace(id)) appIds.Add(id);
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("MainWindow.ReadAppListFiles", ex);
                ShowToast("Failed to read AppList files.", false);
                return;
            }

            if (appIds.Count == 0)
            {
                ShowToast("No games found in AppList.", false);
                return;
            }

            if (_currentProfile == null)
            {
                ShowToast("No profile selected.", false);
                return;
            }

            var existingAppIds = new HashSet<string>(_games.Select(g => g.AppId));
            existingAppIds.UnionWith(_games.SelectMany(g => g.Depots));

            var newAppIds = appIds.Where(id => !existingAppIds.Contains(id)).ToList();

            if (newAppIds.Count == 0)
            {
                ShowToast("All AppList items are already in this profile.");
                return;
            }

            var confirmResult = CustomMessageBox.Show(
                $"Found {newAppIds.Count} new item(s). Would you like to add them to the '{_currentProfile.Name}' profile?",
                "Load AppList",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            var totalFound = newAppIds.Count;
            var progress = new Progress<string>(msg => ShowToast(msg));

            var (importedGames, depotsAddedCount) =
                await ResolveAndImportAppIdsAsync(newAppIds, _games, progress);

            foreach (var game in importedGames.OrderBy(g => g.Name)) _games.Add(game);

            SaveCurrentProfile();
            UpdateGameListState();

            var totalDepotsIncluded = importedGames.Sum(g => g.Depots.Count) + depotsAddedCount;
            ShowToast($"Added {importedGames.Count} Games/DLCs & {totalDepotsIncluded} Depots from {totalFound} IDs");
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.LoadAppList", ex);
        }
    }

    private void ClearProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProfile == null)
        {
            ShowToast("No profile selected", false);
            return;
        }

        if (_games.Count == 0)
        {
            ShowToast("Profile is already empty");
            return;
        }

        var result = CustomMessageBox.Show(
            $"Remove all {_games.Count} game(s) from '{_currentProfile.Name}'?",
            "Clear Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Exclamation);

        if (result != MessageBoxResult.Yes) return;

        foreach (var game in _games.ToList()) IconCacheService.DeleteCachedIcon(game.AppId);

        _games.Clear();
        SaveCurrentProfile();
        UpdateGameListState();
        ShowToast($"Profile '{_currentProfile.Name}' cleared");
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProfile == null || _currentProfile.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("Cannot delete default profile", false);
            return;
        }

        var result = CustomMessageBox.Show(
            $"Delete profile '{_currentProfile.Name}'?",
            "Delete Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Exclamation);

        if (result != MessageBoxResult.Yes)
            return;

        var deletedName = _currentProfile.Name;

        ProfileService.Delete(_currentProfile.Name);
        _profiles.Remove(_currentProfile.Name);

        if (_profiles.Count == 1 && !_profiles.Contains("__empty__")) _profiles.Add("__empty__");

        CmbProfile.SelectedIndex = 0;
        ShowToast($"Deleted profile '{deletedName}'");
    }

    private void RemoveGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Game result)
            return;

        var game = _games.FirstOrDefault(g => g.AppId == result.AppId);

        if (game == null)
            return;

        IconCacheService.DeleteCachedIcon(game.AppId);

        _games.Remove(game);
        ShowToast("Game removed from profile");
        SaveCurrentProfile();
        UpdateGameListState();
    }

    private void UpdateGameListState()
    {
        TxtGameCount.Text = _games.Count.ToString();
        PnlEmptyGames.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StealthMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_config == null || sender is not ToggleButton toggleButton)
            return;

        _config.NoHook = toggleButton.IsChecked.GetValueOrDefault();
        ConfigService.Save(_config);

        UpdateStatusIndicator();
    }

    private async void GenerateApplistButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ValidatePathsForGeneration())
                return;

            if (_currentProfile == null)
            {
                ShowToast("No profile selected", false);
                return;
            }

            if (_games.Count == 0)
            {
                var result = CustomMessageBox.Show(
                    "This profile contains no games. Clear the existing AppList?",
                    "Clear AppList",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            BtnGenerateApplist.IsEnabled = false;

            try
            {
                SaveCurrentProfile();

                var totalAppIds = await GreenLumaService.GenerateAppListAsync(_currentProfile, _config)
                    .ConfigureAwait(true);

                if (totalAppIds >= 0)
                {
                    var generatedCount = Math.Min(totalAppIds, GreenLumaService.AppListLimit);

                    if (generatedCount > 0)
                    {
                        var itemWord = generatedCount == 1 ? "item" : "items";
                        ShowToast($"Generated AppList with {generatedCount} {itemWord}");
                    }
                    else
                    {
                        ShowToast("AppList cleared successfully");
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
                            MessageBoxImage.Exclamation);
                    }
                }
                else
                {
                    ShowToast("Failed to generate AppList - check paths in settings", false);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message, false);
            }
            finally
            {
                BtnGenerateApplist.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.GenerateApplist", ex);
        }
    }

    private async void LaunchGreenlumaButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ValidatePathsForLaunch())
                return;

            if (!await CheckAndGenerateAppList().ConfigureAwait(true))
                return;

            var result = CustomMessageBox.Show(
                "This will close Steam and launch GreenLuma. Continue?",
                "Launch GreenLuma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            BtnLaunchGreenluma.IsEnabled = false;

            try
            {
                if (_config != null && await GreenLumaService.LaunchGreenLumaAsync(_config).ConfigureAwait(true))
                    ShowToast("GreenLuma launched successfully");
                else
                    ShowToast("Failed to launch GreenLuma. Check settings.", false);
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message, false);
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

    private bool ValidatePathsForGeneration()
    {
        if (_config == null || string.IsNullOrWhiteSpace(_config.GreenLumaPath) ||
            string.IsNullOrWhiteSpace(_config.SteamPath))
        {
            var result = CustomMessageBox.Show(
                "Steam and GreenLuma paths must be configured first.\n\nOpen settings now?",
                "Paths Not Set",
                MessageBoxButton.YesNo,
                MessageBoxImage.Exclamation);

            if (result == MessageBoxResult.Yes) SettingsButton_Click(null, null!);

            return false;
        }

        return true;
    }

    private bool ValidatePathsForLaunch()
    {
        return ValidatePathsForGeneration();
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

        switch (result)
        {
            case MessageBoxResult.Cancel:
                return false;

            case MessageBoxResult.Yes:
                GenerateApplistButton_Click(BtnGenerateApplist, new RoutedEventArgs());
                await Task.Delay(500);
                break;
        }

        return true;
    }

    private void ShowToast(string message, bool isSuccess = true)
    {
        ToastMessage.Text = message;

        if (ToastIcon != null)
        {
            var successBrush = Resources["Success"] as Brush ?? Brushes.Green;
            var dangerBrush = Resources["Danger"] as Brush ?? Brushes.Red;
            ToastIcon.Fill = isSuccess ? successBrush : dangerBrush;
        }

        Toast.Visibility = Visibility.Visible;
        Toast.Opacity = 0.0;

        var storyboard = new Storyboard();

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
        Storyboard.SetTarget(fadeIn, Toast);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(ToastDurationMs)
        };
        Storyboard.SetTarget(fadeOut, Toast);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeOut);

        storyboard.Completed += (_, _) =>
        {
            Toast.Visibility = Visibility.Collapsed;
            Toast.Opacity = 1.0;
        };

        storyboard.Begin();
    }

    private static void LaunchBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private async Task<(ConcurrentBag<Game> Games, int DepotsAdded)> ResolveAndImportAppIdsAsync(
        List<string> newAppIds,
        IEnumerable<Game> existingGames,
        IProgress<string>? progress = null)
    {
        var gameLikeAppIds = newAppIds.Where(id => id.EndsWith('0')).ToList();

        var allFoundDepotIds = new HashSet<string>();
        var packageInfos = new ConcurrentDictionary<string, AppPackageInfo>();

        var semaphore = new SemaphoreSlim(6);
        var tasks = new List<Task>();
        var resolvedCount = 0;

        progress?.Report($"Resolving depots... 0/{gameLikeAppIds.Count}");

        foreach (var id in gameLikeAppIds)
        {
            await semaphore.WaitAsync().ConfigureAwait(true);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var info = await DepotService.FetchAppPackageInfoAsync(id).ConfigureAwait(false);
                    if (info != null)
                    {
                        packageInfos[id] = info;
                        foreach (var depotId in info.Depots) allFoundDepotIds.Add(depotId);
                        foreach (var depotList in info.DlcDepots.Values)
                        foreach (var depotId in depotList)
                            allFoundDepotIds.Add(depotId);
                    }

                    var count = Interlocked.Increment(ref resolvedCount);
                    progress?.Report($"Resolving depots... {count}/{gameLikeAppIds.Count}");
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
        tasks.Clear();

        var mainAppIdsToCreate = newAppIds
            .Where(id => !allFoundDepotIds.Contains(id))
            .ToList();

        var importedGames = new ConcurrentBag<Game>();
        var importedCount = 0;

        progress?.Report($"Importing games... 0/{mainAppIdsToCreate.Count}");

        foreach (var id in mainAppIdsToCreate)
        {
            await semaphore.WaitAsync().ConfigureAwait(true);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var info = await DepotService.FetchAppPackageInfoAsync(id).ConfigureAwait(false);
                    if (info == null) return;

                    var game = new Game { AppId = id, Name = string.Empty, Type = "Game" };
                    await SearchService.PopulateGameDetailsAsync(game).ConfigureAwait(false);

                    List<string>? depotsToAssign = null;
                    var parentGameInfo = packageInfos.Values.FirstOrDefault(p => p.DlcAppIds.Contains(id));

                    if (parentGameInfo != null)
                    {
                        if (parentGameInfo.DlcDepots.TryGetValue(id, out var dlcDepots)) depotsToAssign = dlcDepots;
                    }
                    else if (packageInfos.TryGetValue(id, out var selfInfo))
                    {
                        if (selfInfo.Depots.Count > 0)
                            depotsToAssign = selfInfo.Depots;
                        else if (selfInfo.DlcDepots.TryGetValue(id, out var dlcDepots)) depotsToAssign = dlcDepots;
                    }

                    if (depotsToAssign != null)
                        game.Depots = depotsToAssign
                            .Where(depotId => newAppIds.Contains(depotId))
                            .ToList();

                    if (!string.IsNullOrWhiteSpace(game.IconUrl))
                    {
                        var path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId, game.IconUrl)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(path)) game.IconUrl = path;
                    }

                    importedGames.Add(game);

                    var count = Interlocked.Increment(ref importedCount);
                    progress?.Report($"Importing games... {count}/{mainAppIdsToCreate.Count}");
                }
                catch (Exception ex)
                {
                    LogService.LogError("MainWindow.ResolveAndImport", ex);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);

        var depotsAddedCount = 0;
        var allExisting = existingGames.Concat(importedGames);

        foreach (var depotId in newAppIds.Where(id => allFoundDepotIds.Contains(id)))
        {
            string? parentAppId = null;
            foreach (var info in packageInfos.Values)
            {
                if (info.Depots.Contains(depotId))
                {
                    parentAppId = info.AppId;
                    break;
                }

                if (parentAppId != null) break;

                foreach (var dlcDepotPair in info.DlcDepots)
                    if (dlcDepotPair.Value.Contains(depotId))
                    {
                        parentAppId = dlcDepotPair.Key;
                        break;
                    }

                if (parentAppId != null) break;
            }

            if (parentAppId != null)
            {
                var parentGame = allExisting.FirstOrDefault(g => g.AppId == parentAppId);
                if (parentGame != null && !parentGame.Depots.Contains(depotId))
                {
                    parentGame.Depots.Add(depotId);
                    depotsAddedCount++;
                }
            }
        }

        return (importedGames, depotsAddedCount);
    }

    private async Task ImportExistingAppListAsync()
    {
        if (_config == null) return;

        var steamAppListPath = !string.IsNullOrWhiteSpace(_config.SteamPath)
            ? Path.Combine(_config.SteamPath, "AppList")
            : null;
        var greenLumaAppListPath = !string.IsNullOrWhiteSpace(_config.GreenLumaPath)
            ? Path.Combine(_config.GreenLumaPath, "AppList")
            : null;

        var steamHasAppList = steamAppListPath != null && Directory.Exists(steamAppListPath) &&
                              Directory.GetFiles(steamAppListPath, "*.txt").Length > 0;
        var greenLumaHasAppList = greenLumaAppListPath != null && Directory.Exists(greenLumaAppListPath) &&
                                  Directory.GetFiles(greenLumaAppListPath, "*.txt").Length > 0;

        if (!steamHasAppList && !greenLumaHasAppList)
            return;

        var appListToImport = steamHasAppList ? steamAppListPath! : greenLumaAppListPath!;
        var showSteamWarning = steamHasAppList;

        var appIds = new HashSet<string>();
        try
        {
            foreach (var file in Directory.GetFiles(appListToImport, "*.txt"))
            {
                var appId = (await File.ReadAllTextAsync(file)).Trim();
                if (!string.IsNullOrWhiteSpace(appId)) appIds.Add(appId);
            }
        }
        catch (Exception ex)
        {
            LogService.LogError("MainWindow.ReadExistingAppList", ex);
            return;
        }

        if (appIds.Count == 0)
            return;

        var result = CustomMessageBox.Show(
            $"Found {appIds.Count} items in existing AppList.\n\n" +
            "Would you like to import them into your default profile?",
            "Import Existing AppList",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        var defaultProfile = ProfileService.Load("default") ?? new Profile { Name = "default" };
        var existingAppIds = new HashSet<string>(defaultProfile.Games.Select(g => g.AppId));
        existingAppIds.UnionWith(defaultProfile.Games.SelectMany(g => g.Depots));

        var newAppIds = appIds.Where(id => !existingAppIds.Contains(id)).ToList();

        if (newAppIds.Count == 0)
        {
            ShowToast("All games already in profile");
            return;
        }

        var totalFound = newAppIds.Count;
        var progress = new Progress<string>(msg => ShowToast(msg));

        var (importedGames, depotsAddedCount) =
            await ResolveAndImportAppIdsAsync(newAppIds, defaultProfile.Games, progress);

        foreach (var game in importedGames) defaultProfile.Games.Add(game);

        ProfileService.Save(defaultProfile);

        if (CmbProfile.SelectedItem?.ToString() == "default") LoadProfile("default");

        var totalDepotsIncluded = importedGames.Sum(g => g.Depots.Count) + depotsAddedCount;
        ShowToast($"Added {importedGames.Count} Games/DLCs & {totalDepotsIncluded} Depots from {totalFound} IDs");

        if (showSteamWarning)
            CustomMessageBox.Show(
                "WARNING: AppList was found in your Steam folder.\n\n" +
                "For better stealth, you should uninstall GreenLuma from the Steam folder " +
                "and use it from a separate location instead.",
                "Stealth Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
    }

    private void GameName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not TextBlock textBlock || textBlock.DataContext is not Game game)
            return;

        if (game.IsEditing)
            return;

        _editingOriginalName = game.Name;
        game.IsEditing = true;
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

        if (e.Key == Key.Enter)
        {
            CommitNameEdit(game, textBox.Text);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelNameEdit(game);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void GameNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: Game { IsEditing: true } game } textBox)
            CommitNameEdit(game, textBox.Text);
    }

    private void CommitNameEdit(Game game, string newName)
    {
        var trimmedName = newName.Trim();

        if (trimmedName == _editingOriginalName)
        {
            game.IsEditing = false;
            _editingOriginalName = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ShowToast("Game name cannot be empty", false);
            game.Name = _editingOriginalName ?? game.Name;
            game.IsEditing = false;
            _editingOriginalName = null;
            return;
        }

        if (trimmedName.Length > 200)
        {
            ShowToast("Game name is too long", false);
            game.Name = _editingOriginalName ?? game.Name;
            game.IsEditing = false;
            _editingOriginalName = null;
            return;
        }

        game.Name = trimmedName;
        game.IsEditing = false;
        _editingOriginalName = null;
        SaveCurrentProfile();
        ShowToast("Game name updated");
    }

    private void CancelNameEdit(Game game)
    {
        if (_editingOriginalName != null)
        {
            game.Name = _editingOriginalName;
            _editingOriginalName = null;
        }

        game.IsEditing = false;
    }

    private void UpdateStatusIndicator()
    {
        if (_config == null)
        {
            SetStatusIndicator(Resources["Danger"] as Brush ?? Brushes.Red, "Not Configured");
            return;
        }

        var steamPath = _config.SteamPath.Trim();
        var greenLumaPath = _config.GreenLumaPath.Trim();

        if (string.IsNullOrWhiteSpace(steamPath) || string.IsNullOrWhiteSpace(greenLumaPath))
        {
            SetStatusIndicator(Resources["Danger"] as Brush ?? Brushes.Red, "Not Configured");
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
            SetStatusIndicator(successBrush, "Ready  •  Normal Mode");
        else if (_config.NoHook)
            SetStatusIndicator(successBrush, isStealthOnly ? "Ready  •  Stealth Mode (Forced)" : "Ready  •  Stealth Mode");
        else
            SetStatusIndicator(successBrush, "Ready  •  Normal Mode");
    }

    private void SetStatusIndicator(Brush color, string text)
    {
        var storyboard = new Storyboard();

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150));
        Storyboard.SetTarget(fadeOut, TxtStatus);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));

        fadeOut.Completed += (_, _) =>
        {
            StatusIndicator.Fill = color;
            TxtStatus.Text = text;

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150));
            Storyboard.SetTarget(fadeIn, TxtStatus);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));

            var storyboardIn = new Storyboard();
            storyboardIn.Children.Add(fadeIn);
            storyboardIn.Begin();
        };

        storyboard.Children.Add(fadeOut);
        storyboard.Begin();
    }

    private void ManagePluginsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PluginsDialog
        {
            Owner = this
        };
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

    private void MoveGameUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu ctx ||
            ctx.PlacementTarget is not FrameworkElement fe ||
            fe.DataContext is not Game game)
            return;

        var index = _games.IndexOf(game);
        if (index <= 0) return;
        _games.Move(index, index - 1);
        SaveCurrentProfile();
    }

    private void MoveGameDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu ctx ||
            ctx.PlacementTarget is not FrameworkElement fe ||
            fe.DataContext is not Game game)
            return;

        var index = _games.IndexOf(game);
        if (index < 0 || index >= _games.Count - 1) return;
        _games.Move(index, index + 1);
        SaveCurrentProfile();
    }
}