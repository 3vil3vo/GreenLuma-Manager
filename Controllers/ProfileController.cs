using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GreenLuma_Manager.Dialogs;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;
using Microsoft.Win32;

namespace GreenLuma_Manager.Controllers;

public class ProfileController
{
    private readonly ComboBox _cmbProfile;
    private readonly ObservableCollection<string> _profiles;
    private readonly GameListController _gameListController;
    private readonly GreenLumaLauncher _launcher;
    private readonly NotificationManager _notificationManager;

    public Profile? CurrentProfile { get; private set; }
    public Config? Config { get; set; }

    public ProfileController(
        ComboBox cmbProfile,
        ObservableCollection<string> profiles,
        GameListController gameListController,
        GreenLumaLauncher launcher,
        NotificationManager notificationManager)
    {
        _cmbProfile = cmbProfile;
        _profiles = profiles;
        _gameListController = gameListController;
        _launcher = launcher;
        _notificationManager = notificationManager;
    }

    public void LoadProfileList()
    {
        _profiles.Clear();

        var availableProfiles = ProfileService.LoadAll().Select(p => p.Name).ToList();
        foreach (var profile in availableProfiles)
            _profiles.Add(profile);

        if (_profiles.Count == 0)
            _profiles.Add("default");

        var lastProfile = Config?.LastProfile ?? "default";

        if (_profiles.Contains(lastProfile))
            _cmbProfile.SelectedItem = lastProfile;
        else
            _cmbProfile.SelectedIndex = 0;

        if (_profiles.Count == 1)
            _profiles.Add("__empty__");
    }

    public void SelectProfile(string? profileName)
    {
        if (profileName == "__empty__")
            return;

        if (profileName != null)
            LoadProfile(profileName);
    }

    public void LoadProfile(string profileName)
    {
        CurrentProfile = ProfileService.Load(profileName);

        if (CurrentProfile == null)
        {
            CurrentProfile = new Profile { Name = profileName };
            ProfileService.Save(CurrentProfile);
        }

        _gameListController.LoadGames(CurrentProfile.Games);

        if (Config != null)
        {
            Config.LastProfile = profileName;
            ConfigService.Save(Config);
        }
    }

    public void SaveCurrentProfile()
    {
        if (CurrentProfile == null)
            return;

        CurrentProfile.Games.Clear();
        foreach (var game in _gameListController.Games)
            CurrentProfile.Games.Add(game);

        ProfileService.Save(CurrentProfile);
    }

    public string CreateProfile()
    {
        var dialog = new CreateProfileDialog
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        };

        if (dialog.ShowDialog() != true || dialog.Result == null)
            return string.Empty;

        var newProfile = dialog.Result;

        _profiles.Remove("__empty__");
        _profiles.Add(newProfile.Name);

        ProfileService.Save(newProfile);

        _cmbProfile.SelectedItem = newProfile.Name;
        return newProfile.Name;
    }

    public void DeleteProfile(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) || profileName == "default")
        {
            _notificationManager.ShowToast("Cannot delete the default profile", false);
            return;
        }

        var result = CustomMessageBox.Show(
            $"Delete profile '{profileName}'?\n\nThis cannot be undone.",
            "Delete Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        ProfileService.Delete(profileName);
        _profiles.Remove(profileName);

        _cmbProfile.SelectedItem = "default";
        _notificationManager.ShowToast($"Profile '{profileName}' deleted");
    }

    public void ImportProfile()
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
            _notificationManager.ShowToast("Failed to import profile", false);
            return;
        }

        foreach (var game in profile.Games)
            game.IconUrl = string.Empty;

        if (!ValidateProfileName(profile.Name))
            return;

        if (_profiles.Contains(profile.Name))
        {
            var overwriteResult = CustomMessageBox.Show(
                $"Profile '{profile.Name}' already exists. Overwrite?",
                "Import Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (overwriteResult != MessageBoxResult.Yes)
                return;
        }
        else
        {
            _profiles.Remove("__empty__");
            _profiles.Add(profile.Name);
        }

        ProfileService.Save(profile);

        if (_cmbProfile.SelectedItem?.ToString() == profile.Name)
            LoadProfile(profile.Name);
        else
            _cmbProfile.SelectedItem = profile.Name;

        _notificationManager.ShowToast($"Imported profile '{profile.Name}'");
    }

    public void ExportProfile()
    {
        if (CurrentProfile == null)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files|*.json|All files|*.*",
            FileName = $"{CurrentProfile.Name}.json",
            Title = "Export Profile"
        };

        if (dialog.ShowDialog() != true)
            return;

        SaveCurrentProfile();

        var exportProfile = ProfileService.Load(CurrentProfile.Name);
        if (exportProfile != null)
        {
            ProfileService.Export(exportProfile, dialog.FileName);
            _notificationManager.ShowToast($"Exported '{CurrentProfile.Name}'");
        }
        else
        {
            _notificationManager.ShowToast("Failed to export profile", false);
        }
    }

    private static bool ValidateProfileName(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) || profileName.Length > 50)
        {
            return false;
        }

        if (profileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            profileName.Contains('/') || profileName.Contains('\\'))
        {
            return false;
        }

        return true;
    }
}
