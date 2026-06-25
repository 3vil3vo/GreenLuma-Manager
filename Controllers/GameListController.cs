using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GreenLuma_Manager.Models;

namespace GreenLuma_Manager.Controllers;

public class GameListController
{
    private readonly NotificationManager _notificationManager;
    private readonly UIElement _pnlEmptyGames;

    private string? _searchFilter;
    private string? _typeFilter;

    public GameListController(ItemsControl lstGames, UIElement pnlEmptyGames, NotificationManager notificationManager)
    {
        _pnlEmptyGames = pnlEmptyGames;
        _notificationManager = notificationManager;

        lstGames.ItemsSource = CollectionViewSource.GetDefaultView(Games);
    }

    public ObservableCollection<Game> Games { get; } = [];

    public string? EditingOriginalName { get; set; }

    public bool IsFilterActive => !string.IsNullOrEmpty(_searchFilter);
    public bool IsTypeFilterActive => !string.IsNullOrEmpty(_typeFilter) && _typeFilter != "All";

    public void SetSearchFilter(string? filter)
    {
        _searchFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        ApplyFilters();
    }

    public void SetTypeFilter(string? type)
    {
        _typeFilter = type;
        ApplyFilters();
    }

    public void ApplyFilters()
    {
        var view = CollectionViewSource.GetDefaultView(Games);

        if (!IsFilterActive && !IsTypeFilterActive)
        {
            view.Filter = null;
            _notificationManager.UpdateGameCount(Games.Count);
            UpdateGameListState();
            return;
        }

        var searchLower = _searchFilter?.ToLowerInvariant();
        var typeFilter = IsTypeFilterActive ? _typeFilter : null;

        view.Filter = item =>
        {
            if (item is not Game game) return false;

            if (searchLower != null)
            {
                var normalizedName = NormalizeForSearch(game.Name.ToLowerInvariant());
                if (!normalizedName.Contains(searchLower)) return false;
            }

            if (typeFilter != null)
            {
                if (string.Equals(typeFilter, "Other", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(game.Type, "Game", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(game.Type, "DLC", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                else if (!string.Equals(game.Type, typeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        };

        var filteredCount = view.Cast<object>().Count();
        _notificationManager.UpdateGameCount(filteredCount, true);
        UpdateGameListState();
    }

    private static string NormalizeForSearch(string input)
    {
        return input
            .Replace("'", "")
            .Replace("ʻ", "")
            .Replace("ʼ", "")
            .Replace("'", "");
    }

    public void ClearGames()
    {
        Games.Clear();
        _notificationManager.UpdateGameCount(0);
        UpdateGameListState();
    }

    public void AddGame(Game game)
    {
        var index = BinarySearchInsertIndex(game.Name);
        Games.Insert(index, game);

        var view = CollectionViewSource.GetDefaultView(Games);
        var count = view.Filter == null ? Games.Count : view.Cast<object>().Count();
        _notificationManager.UpdateGameCount(count, view.Filter != null);
        UpdateGameListState();
    }

    public void RemoveGame(Game game)
    {
        Games.Remove(game);

        var view = CollectionViewSource.GetDefaultView(Games);
        var count = view.Filter == null ? Games.Count : view.Cast<object>().Count();
        _notificationManager.UpdateGameCount(count, view.Filter != null);
        UpdateGameListState();
    }

    public void LoadGames(IEnumerable<Game> games)
    {
        Games.Clear();
        _searchFilter = null;
        _typeFilter = null;
        CollectionViewSource.GetDefaultView(Games).Filter = null;

        foreach (var game in games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Games.Add(game);

        _notificationManager.UpdateGameCount(Games.Count);
        UpdateGameListState();
    }

    public void UpdateGameListState()
    {
        _pnlEmptyGames.Visibility = Games.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void StartRename(Game game)
    {
        if (game.IsEditing) return;
        EditingOriginalName = game.Name;
        game.IsEditing = true;
    }

    public void CommitRename(Game game, string? newName)
    {
        game.IsEditing = false;
        var trimmed = newName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == EditingOriginalName)
        {
            if (EditingOriginalName != null) game.Name = EditingOriginalName;
            EditingOriginalName = null;
            return;
        }

        game.Name = trimmed;
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
        return Games.Select(g => g.AppId).ToList();
    }

    public void MoveUp(Game game)
    {
        var idx = Games.IndexOf(game);
        if (idx > 0) Games.Move(idx, idx - 1);
    }

    public void MoveDown(Game game)
    {
        var idx = Games.IndexOf(game);
        if (idx >= 0 && idx < Games.Count - 1) Games.Move(idx, idx + 1);
    }

    private int BinarySearchInsertIndex(string name)
    {
        var lo = 0;
        var hi = Games.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (string.Compare(Games[mid].Name, name, StringComparison.OrdinalIgnoreCase) <= 0)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }
}