using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;
using GreenLuma_Manager.Utilities;
using Microsoft.Win32;

namespace GreenLuma_Manager.Dialogs;

public partial class SettingsDialog
{
    private readonly Config _config;

    public SettingsDialog(Config config)
    {
        InitializeComponent();
        _config = config;

        LoadSettings();
        UpdateAutoUpdateVisibility();

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void LoadSettings()
    {
        TxtSteamPath.Text = _config.SteamPath;
        TxtGreenLumaPath.Text = _config.GreenLumaPath;
        TxtSteamApiKey.Text = _config.SteamApiKey;
        ChkReplaceSteamAutostart.IsChecked = _config.ReplaceSteamAutostart;
        ChkPrefetchAppList.IsChecked = _config.PrefetchAppList;
        ChkStartSteamMinimized.IsChecked = _config.StartSteamMinimized;
        ChkDisableUpdateCheck.IsChecked = _config.DisableUpdateCheck;
        ChkAutoUpdate.IsChecked = _config.AutoUpdate;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel_Click(this, new RoutedEventArgs());
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewGeneral == null || ViewSystem == null || ViewAdvanced == null) return;

        ViewGeneral.Visibility = Visibility.Collapsed;
        ViewSystem.Visibility = Visibility.Collapsed;
        ViewAdvanced.Visibility = Visibility.Collapsed;

        if (NavGeneral.IsChecked == true) ShowView(ViewGeneral);
        else if (NavSystem.IsChecked == true) ShowView(ViewSystem);
        else if (NavAdvanced.IsChecked == true) ShowView(ViewAdvanced);
    }

    private static void ShowView(UIElement view)
    {
        view.Visibility = Visibility.Visible;
    }

    private void BrowseSteam_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Steam folder"
        };

        if (!string.IsNullOrWhiteSpace(TxtSteamPath.Text) && Directory.Exists(TxtSteamPath.Text))
            dialog.InitialDirectory = TxtSteamPath.Text;

        if (dialog.ShowDialog() == true) TxtSteamPath.Text = dialog.FolderName;
    }

    private void BrowseGreenLuma_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select GreenLuma folder"
        };

        if (!string.IsNullOrWhiteSpace(TxtGreenLumaPath.Text) && Directory.Exists(TxtGreenLumaPath.Text))
            dialog.InitialDirectory = TxtGreenLumaPath.Text;

        if (dialog.ShowDialog() == true) TxtGreenLumaPath.Text = dialog.FolderName;
    }

    private void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        var (steamPath, greenLumaPath) = PathDetector.DetectPaths();

        TxtSteamPath.Text = steamPath;
        TxtGreenLumaPath.Text = greenLumaPath;

        if (!string.IsNullOrWhiteSpace(steamPath) && !string.IsNullOrWhiteSpace(greenLumaPath))
            CustomMessageBox.Show("Paths detected successfully!", "Success", icon: MessageBoxImage.Asterisk);
        else
            CustomMessageBox.Show("Could not detect all paths automatically.", "Detection",
                icon: MessageBoxImage.Exclamation);
    }

    private void DisableUpdateCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAutoUpdateVisibility();
    }

    private void UpdateAutoUpdateVisibility()
    {
        if (ChkAutoUpdate == null || ChkDisableUpdateCheck == null)
            return;

        var isEnabled = !ChkDisableUpdateCheck.IsChecked.GetValueOrDefault();
        ChkAutoUpdate.IsEnabled = isEnabled;

        if (!isEnabled) ChkAutoUpdate.IsChecked = false;
    }

    private void WipeData_Click(object sender, RoutedEventArgs e)
    {
        if (CustomMessageBox.Show("This will delete all profiles and settings. Continue?", "Wipe Data",
                MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
            return;

        if (CustomMessageBox.Show("Are you absolutely sure? This cannot be undone.", "Confirm Wipe",
                MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
            return;

        ConfigService.WipeData();
        CustomMessageBox.Show("All data has been wiped. The application will now close.", "Complete",
            icon: MessageBoxImage.Asterisk);
        Application.Current.Shutdown();
    }

    private void OpenAppData_Click(object sender, RoutedEventArgs e)
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GLM_Manager");

        if (Directory.Exists(appDataDir))
            Process.Start(new ProcessStartInfo { FileName = appDataDir, UseShellExecute = true });
        else
            CustomMessageBox.Show("App data folder does not exist yet.", "Info",
                icon: MessageBoxImage.Information);
    }

    private async void RestartSteam_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var steamExePath = Path.Combine(_config.SteamPath, "Steam.exe");
            if (!File.Exists(steamExePath))
            {
                CustomMessageBox.Show("Steam executable not found at the configured path.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string[] processNames = ["steam", "steamservice", "steamwebhelper"];

            Process.Start(new ProcessStartInfo
            {
                FileName = steamExePath,
                Arguments = "-shutdown",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            await Task.Delay(3000);

            foreach (var name in processNames)
            foreach (var proc in Process.GetProcessesByName(name))
                try
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }

            Process.Start(new ProcessStartInfo
            {
                FileName = steamExePath,
                UseShellExecute = true
            });

            CustomMessageBox.Show("Steam has been restarted.", "Done",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogService.LogError("SettingsDialog.RestartSteam", ex);
            CustomMessageBox.Show("Failed to restart Steam: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().TrimEnd('\\', '/');
    }

    private static bool ValidatePaths(string steamPath, string greenLumaPath)
    {
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            CustomMessageBox.Show("Steam path cannot be empty.", "Validation", icon: MessageBoxImage.Exclamation);
            return false;
        }

        if (string.IsNullOrWhiteSpace(greenLumaPath))
        {
            CustomMessageBox.Show("GreenLuma path cannot be empty.", "Validation", icon: MessageBoxImage.Exclamation);
            return false;
        }

        if (!Directory.Exists(steamPath))
        {
            CustomMessageBox.Show("Steam path does not exist.", "Validation", icon: MessageBoxImage.Exclamation);
            return false;
        }

        var steamExePath = Path.Combine(steamPath, "Steam.exe");
        if (!File.Exists(steamExePath))
        {
            CustomMessageBox.Show($"Steam.exe not found at:\n{steamExePath}", "Validation",
                icon: MessageBoxImage.Exclamation);
            return false;
        }

        if (!Directory.Exists(greenLumaPath))
        {
            CustomMessageBox.Show($"GreenLuma path does not exist:\n{greenLumaPath}", "Validation",
                icon: MessageBoxImage.Exclamation);
            return false;
        }

        if (string.Equals(Path.GetFullPath(steamPath), Path.GetFullPath(greenLumaPath),
                StringComparison.OrdinalIgnoreCase))
        {
            var result = CustomMessageBox.Show(
                "Installing GreenLuma in the Steam directory is not recommended. Some games scan this location for GreenLuma files, which may result in detection.\n\n" +
                "Do you want to continue anyway?",
                "Security Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return false;
        }

        if (IsPathReadOnly(greenLumaPath))
        {
            CustomMessageBox.Show(
                $"The GreenLuma path is read-only.\nPlease ensure the folder is writable and not marked as Read-Only.\nPath: {greenLumaPath}",
                "Validation",
                icon: MessageBoxImage.Exclamation);
            return false;
        }

        var (isValid, _, missingFiles) = GreenLumaService.ValidateInstallation(greenLumaPath);
        if (!isValid)
        {
            CustomMessageBox.Show(
                $"GreenLuma installation is incomplete.\nThe following files are missing or invalid:\n\n{string.Join("\n", missingFiles)}",
                "Validation",
                icon: MessageBoxImage.Exclamation);
            return false;
        }

        return true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var steamPath = NormalizePath(TxtSteamPath.Text);
        var greenLumaPath = NormalizePath(TxtGreenLumaPath.Text);

        if (!ValidatePaths(steamPath, greenLumaPath)) return;

        var (_, isStealthOnly, _) = GreenLumaService.ValidateInstallation(greenLumaPath);
        if (isStealthOnly)
        {
            CustomMessageBox.Show(
                "Only files required for Stealth Mode were detected.\n" +
                "The application will be locked to Stealth Mode.\n\n" +
                "Warning: Some GreenLuma features may not work without the full installation.",
                "Stealth Mode Only",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _config.NoHook = true;
        }

        _config.SteamPath = steamPath;
        _config.GreenLumaPath = greenLumaPath;
        _config.SteamApiKey = TxtSteamApiKey.Text.Trim();
        _config.ReplaceSteamAutostart = ChkReplaceSteamAutostart.IsChecked.GetValueOrDefault();
        _config.PrefetchAppList = ChkPrefetchAppList.IsChecked.GetValueOrDefault();
        _config.StartSteamMinimized = ChkStartSteamMinimized.IsChecked.GetValueOrDefault();
        _config.DisableUpdateCheck = ChkDisableUpdateCheck.IsChecked.GetValueOrDefault();
        _config.AutoUpdate = ChkAutoUpdate.IsChecked.GetValueOrDefault();
        ConfigService.Save(_config);
        AutostartManager.ManageAutostart(_config.ReplaceSteamAutostart, _config);

        DialogResult = true;
        Close();
    }

    private static bool IsPathReadOnly(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                return true;

            var tempFile = Path.Combine(path, Path.GetRandomFileName());
            using (File.Create(tempFile, 1, FileOptions.DeleteOnClose))
            {
            }

            return false;
        }
        catch (Exception ex)
        {
            LogService.LogError("SettingsDialog.IsReadOnly", ex);
            return true;
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}