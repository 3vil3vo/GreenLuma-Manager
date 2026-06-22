using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;

namespace GreenLuma_Manager.Controllers;

public class GameListController
{
    private readonly ItemsControl _lstGames;
    private readonly TextBlock _txtGameCount;
    private readonly UIElement _pnlEmptyGames;
    private readonly NotificationManager _notificationManager;

    public ObservableCollection<Game> Games { get; }
    public string? EditingOriginalName { get; set; }

    public Game? CurrentSelection { get; set; }

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
        _lstGames.ItemsSource = Games;
    }

    public void ClearGames()
    {
        Games.Clear();
        UpdateGameListState();
    }

    public void AddGame(Game game)
    {
        Games.Add(game);
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
        foreach (var game in games)
            Games.Add(game);

        _notificationManager.UpdateGameCount(Games.Count);
        UpdateGameListState();
    }

    public void UpdateGameListState()
    {
        _pnlEmptyGames.Visibility = Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
