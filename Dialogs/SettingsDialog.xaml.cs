using GreenLuma_Manager.Models;
using GreenLuma_Manager.Services;
using GreenLuma_Manager.Utilities;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Windows.Controls;

namespace GreenLuma_Manager.Dialogs
{
    public partial class SettingsDialog : Window
    {
        private readonly Config _config;
        private readonly ConfigService _configService;
        private readonly PathDetector _pathDetector;
        private readonly AutostartManager _autostartManager;

        private sealed class LanguageItem
        {
            public LanguageItem(string code, string display)
            {
                Code = code;
                Display = display;
            }
            public string Code { get; }
            public string Display { get; }
        }

        public SettingsDialog(Config config)
        {
            InitializeComponent();

            _config = config;
            _configService = new ConfigService();
            _pathDetector = new PathDetector();
            _autostartManager = new AutostartManager();

            LoadSettings();
            UpdateAutoUpdateVisibility();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void LoadSettings()
        {
            txtSteamPath.Text = _config.SteamPath;
            txtGreenLumaPath.Text = _config.GreenLumaPath;
            chkReplaceSteamAutostart.IsChecked = _config.ReplaceSteamAutostart;
            chkDisableUpdateCheck.IsChecked = _config.DisableUpdateCheck;
            chkAutoUpdate.IsChecked = _config.AutoUpdate;

            InitializeLanguageSelector();
        }

        private void InitializeLanguageSelector()
        {
            try
            {
                var items = new List<LanguageItem>
                {
                    new("en", TryFindString("Lang.English") ?? "English"),
                    new("zh-Hans", TryFindString("Lang.ChineseSimplified") ?? "简体中文"),
                    new("zh-Hant", TryFindString("Lang.ChineseTraditional") ?? "繁體中文"),
                    new("ja", TryFindString("Lang.Japanese") ?? "日本語")
                };

                cmbLanguage.DisplayMemberPath = nameof(LanguageItem.Display);
                cmbLanguage.SelectedValuePath = nameof(LanguageItem.Code);
                cmbLanguage.ItemsSource = items;

                string code = _config.Language;
                if (string.IsNullOrWhiteSpace(code))
                {
                    code = LocalizationService.GetDefaultLanguage();
                }
                cmbLanguage.SelectedValue = code;

                cmbLanguage.SelectionChanged -= CmbLanguage_SelectionChanged;
                cmbLanguage.SelectionChanged += CmbLanguage_SelectionChanged;
            }
            catch
            {
            }
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbLanguage.SelectedValue is string code && !string.IsNullOrWhiteSpace(code))
                {
                    _config.Language = code;
                    ConfigService.Save(_config);
                    LocalizationService.ApplyLanguage(code);
                }
            }
            catch
            {
            }
        }

        private string? TryFindString(string key)
        {
            try
            {
                var obj = TryFindResource(key);
                return obj as string;
            }
            catch
            {
                return null;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancel_Click(this, new RoutedEventArgs());
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BrowseSteam_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Steam folder (e.g., C:\\Program Files (x86)\\Steam)"
            };

            if (!string.IsNullOrWhiteSpace(txtSteamPath.Text) && Directory.Exists(txtSteamPath.Text))
            {
                dialog.InitialDirectory = txtSteamPath.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                txtSteamPath.Text = dialog.FolderName;
            }
        }

        private void BrowseGreenLuma_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select GreenLuma folder (containing DLLInjector.exe)"
            };

            if (!string.IsNullOrWhiteSpace(txtGreenLumaPath.Text) && Directory.Exists(txtGreenLumaPath.Text))
            {
                dialog.InitialDirectory = txtGreenLumaPath.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                txtGreenLumaPath.Text = dialog.FolderName;
            }
        }

        private void AutoDetect_Click(object sender, RoutedEventArgs e)
        {
            var (steamPath, greenLumaPath) = PathDetector.DetectPaths();
            txtSteamPath.Text = steamPath;
            txtGreenLumaPath.Text = greenLumaPath;

            if (!string.IsNullOrWhiteSpace(steamPath) && !string.IsNullOrWhiteSpace(greenLumaPath))
            {
                CustomMessageBox.Show("Paths detected successfully!", "Success", icon: MessageBoxImage.Asterisk);
            }
            else
            {
                CustomMessageBox.Show("Could not detect all paths automatically. Please set them manually.",
                    "Detection", icon: MessageBoxImage.Exclamation);
            }
        }

        private void WipeData_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult confirm1 = CustomMessageBox.Show(
                "This will delete all profiles and settings. Continue?",
                "Wipe Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Exclamation);

            if (confirm1 != MessageBoxResult.Yes)
                return;

            MessageBoxResult confirm2 = CustomMessageBox.Show(
                "Are you absolutely sure? This cannot be undone.",
                "Confirm Wipe",
                MessageBoxButton.YesNo,
                MessageBoxImage.Exclamation);

            if (confirm2 != MessageBoxResult.Yes)
                return;

            ConfigService.WipeData();
            CustomMessageBox.Show("All data has been wiped. The application will now close.", "Complete",
                icon: MessageBoxImage.Asterisk);
            Application.Current.Shutdown();
        }

        private void DisableUpdateCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAutoUpdateVisibility();
        }

        private void UpdateAutoUpdateVisibility()
        {
            if (chkAutoUpdate == null || chkDisableUpdateCheck == null)
                return;

            bool isEnabled = !chkDisableUpdateCheck.IsChecked.GetValueOrDefault();
            chkAutoUpdate.IsEnabled = isEnabled;

            if (!isEnabled)
            {
                chkAutoUpdate.IsChecked = false;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string steamPath = NormalizePath(txtSteamPath.Text);
            string greenLumaPath = NormalizePath(txtGreenLumaPath.Text);

            if (!ValidatePaths(steamPath, greenLumaPath))
                return;

            _config.SteamPath = steamPath;
            _config.GreenLumaPath = greenLumaPath;
            _config.ReplaceSteamAutostart = chkReplaceSteamAutostart.IsChecked.GetValueOrDefault();
            _config.DisableUpdateCheck = chkDisableUpdateCheck.IsChecked.GetValueOrDefault();
            _config.AutoUpdate = chkAutoUpdate.IsChecked.GetValueOrDefault();

            ConfigService.Save(_config);
            AutostartManager.ManageAutostart(_config.ReplaceSteamAutostart, _config);

            DialogResult = true;
            Close();
        }

        private static string NormalizePath(string path)
        {
            if (path == null)
                return string.Empty;
            return path.Trim().TrimEnd('\\', '/');
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
                CustomMessageBox.Show("GreenLuma path cannot be empty.", "Validation",
                    icon: MessageBoxImage.Exclamation);
                return false;
            }

            if (!Directory.Exists(steamPath))
            {
                CustomMessageBox.Show("Steam path does not exist.", "Validation", icon: MessageBoxImage.Exclamation);
                return false;
            }

            string steamExePath = Path.Combine(steamPath, "Steam.exe");
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

            string injectorPath = Path.Combine(greenLumaPath, "DLLInjector.exe");
            if (!File.Exists(injectorPath))
            {
                CustomMessageBox.Show($"DLLInjector.exe not found at:\n{injectorPath}", "Validation",
                    icon: MessageBoxImage.Exclamation);
                return false;
            }

            return true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
