using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPUSetSetter.Config;
using CPUSetSetter.Platforms;
using CPUSetSetter.Themes;
using CPUSetSetter.Util;
using Microsoft.Win32;


namespace CPUSetSetter.UI.Tabs.Settings
{
    public partial class SettingsTabViewModel : ObservableObject
    {
        public static List<Theme> AvailableThemes { get; } = new(Enum.GetValues(typeof(Theme)).Cast<Theme>());

        [ObservableProperty]
        private bool _autoStartEnabled = AutoStarter.IsEnabled;

        [ObservableProperty]
        private string? _configStatusText;

        [RelayCommand]
        private static void OpenReleasePage()
        {
            VersionChecker.OpenLatestReleasePage();
        }

        partial void OnAutoStartEnabledChanged(bool value)
        {
            if (value && !AutoStarter.IsEnabled)
            {
                AutoStartEnabled = AutoStarter.Enable();
            }
            else if (!value && AutoStarter.IsEnabled)
            {
                AutoStarter.Disable();
                AutoStartEnabled = AutoStarter.IsEnabled;
            }
        }

        [RelayCommand]
        private void ExportConfig()
        {
            SaveFileDialog dialog = new()
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "CPUSetSetter_config_export.json",
            };
            if (dialog.ShowDialog() != true)
                return;

            string? error = AppConfigFile.ExportToFile(dialog.FileName);
            ConfigStatusText = error ?? $"Config exported to {dialog.FileName}";
        }

        [RelayCommand]
        private void ImportConfig()
        {
            OpenFileDialog dialog = new()
            {
                Filter = "JSON files (*.json)|*.json",
            };
            if (dialog.ShowDialog() != true)
                return;

            MessageBoxResult choice = MessageBox.Show(
                "Importing will replace your current Masks, Rules, Rule templates and settings.\nContinue?",
                "Import config",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes)
                return;

            string? error = AppConfigFile.ImportFromFile(dialog.FileName);
            if (error is null)
                ConfigStatusText = $"Config imported from {dialog.FileName}";
            else
                ConfigStatusText = error;
        }
    }
}
