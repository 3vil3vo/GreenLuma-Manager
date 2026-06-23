using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;

namespace GreenLuma_Manager.Controllers;

public class GameListController
{
    private readonly ItemsControl _lstGames;
    private readonly TextBlock _txtGameCount;
    private readonly UIElement _pnlEmptyGames;
    private readonly NotificationManager _notificationManager;
    private ICollectionView? _gamesView;
    private string? _searchFilter;

    public ObservableCollection<Game> Games { get; }
    public string? EditingOriginalName { get; set; }

    public Game? CurrentSelection { get; set; }

    public bool IsFilterActive => !string.IsNullOrWhiteSpace(_searchFilter);

    public GameListController(
        ItemsControl lstGames,
        TextBlock txtGameCount,
        UIElement pnlEmptyGames,
        NotificationManager notificationManager)
    {
        _lstGames = lstGames;
        _txtGameCount = txtGameCount;
        _pnlEmptyGames = pnlEmptyGames;
        _notificationManager = notificationManager;

        Games = [];
        _gamesView = CollectionViewSource.GetDefaultView(Games);
        _gamesView.Refresh();
        _lstGames.ItemsSource = _gamesView;
    }

    public void SetSearchFilter(string? searchText)
    {
        _searchFilter = searchText;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            _gamesView!.Filter = null;
            _notificationManager.UpdateGameCount(Games.Count);
        }
        else
        {
            var rawFilter = searchText.Trim();
            var filter = NormalizeForSearch(rawFilter);
            _gamesView!.Filter = obj =>
            {
                if (obj is not Game game) return false;
                var normalizedName = NormalizeForSearch(game.Name);
                return normalizedName.Contains(filter, StringComparison.OrdinalIgnoreCase);
            };

            var filteredCount = 0;
            foreach (var _ in _gamesView!)
                filteredCount++;
            _notificationManager.UpdateGameCount(filteredCount, true);
        }

        UpdateGameListState();
    }

    /// <summary>
    /// Strips characters that commonly cause search mismatches (apostrophes, etc.)
    /// so that searching "Dragons" matches "Dragon's".
    /// </summary>
    private static string NormalizeForSearch(string text)
    {
        return text
            .Replace("'", "")
            .Replace("’", "")
            .Replace("ʻ", "")
            .Replace("ʼ", "");
    }

    public void ClearGames()
    {
        Games.Clear();
        _notificationManager.UpdateGameCount(Games.Count);
        UpdateGameListState();
    }

    public void AddGame(Game game)
    {
        // Insert in alphabetical order by name
        var index = 0;
        while (index < Games.Count && string.Compare(Games[index].Name, game.Name, StringComparison.OrdinalIgnoreCase) <= 0)
            index++;

        Games.Insert(index, game);
        _notificationManager.UpdateGameCount(Games.Count);
        UpdateGameListState();
    }

    public void RemoveGame(Game game)
    {
        Games.Remove(game);
        _notificationManager.UpdateGameCount(Games.Count);
        UpdateGameListState();
    }

    public void LoadGames(IEnumerable<Game> games)
    {
        Games.Clear();
        foreach (var game in games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Games.Add(game);

        // Clear search filter when loading a new set of games
        _searchFilter = null;
        if (_gamesView != null)
            _gamesView.Filter = null;

        _notificationManager.UpdateGameCount(Games.Count);
        UpdateGameListState();
    }

    public void UpdateGameListState()
    {
        var hasItems = Games.Count > 0;

        // Show empty panel only if there are no games at all (not just filtered away)
        _pnlEmptyGames.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ToggleGameCheck(Game game, bool? isChecked)
    {
    }

    public void StartRename(Game game)
    {
        if (game.IsEditing)
            return;

        EditingOriginalName = game.Name;
        game.IsEditing = true;
    }

    public void CommitRename(Game game, string? newName)
    {
        game.IsEditing = false;

        if (string.IsNullOrWhiteSpace(newName) || newName == EditingOriginalName)
        {
            if (EditingOriginalName != null)
                game.Name = EditingOriginalName;
            EditingOriginalName = null;
            return;
        }

        game.Name = newName;
        EditingOriginalName = null;
    }

    public void CancelRename(Game game)
    {
        game.IsEditing = false;
        if (EditingOriginalName != null)
        {
            game.Name = EditingOriginalName;
            EditingOriginalName = null;
        }
    }

    public List<string> GetSelectedAppIds()
    {
        return Games
            .Where(g => g.IsEditing == false)
            .Select(g => g.AppId)
            .ToList();
    }

    public async Task ImportAppIdsAsync(IEnumerable<string> appIds, Profile? profile)
    {
        if (profile == null) return;

        var semaphore = new SemaphoreSlim(6);
        var tasks = new List<Task>();
        var importedGames = new ConcurrentBag<Game>();

        foreach (var id in appIds)
        {
            await semaphore.WaitAsync();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var info = await DepotService.FetchAppPackageInfoAsync(id).ConfigureAwait(false);
                    if (info == null) return;

                    var game = new Game { AppId = id, Name = string.Empty, Type = "Game" };
                    await SearchService.PopulateGameDetailsAsync(game).ConfigureAwait(false);

                    List<string>? depotsToAssign = null;

                    if (info.DlcDepots.TryGetValue(id, out var dlcDepots))
                        depotsToAssign = dlcDepots;
                    else if (info.Depots.Count > 0)
                        depotsToAssign = info.Depots;

                    if (depotsToAssign != null)
                        game.Depots = depotsToAssign
                            .Where(depotId => appIds.Contains(depotId))
                            .ToList();

                    if (!string.IsNullOrWhiteSpace(game.IconUrl))
                    {
                        var path = await IconCacheService.DownloadAndCacheIconAsync(game.AppId, game.IconUrl)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(path)) game.IconUrl = path;
                    }

                    importedGames.Add(game);
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);

        foreach (var game in importedGames.OrderBy(g => int.Parse(g.AppId)))
        {
            if (!Games.Any(g => g.AppId == game.AppId))
            {
                Games.Add(game);
                profile.Games.Add(game);
            }
        }

        _notificationManager.UpdateGameCount(Games.Count);
        UpdateGameListState();
    }

    public int GetUncheckedGameCount()
    {
        return Games.Count(g => g.IsEditing);
    }
}
