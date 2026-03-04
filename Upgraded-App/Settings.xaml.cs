using FishLens_App.Interfaces;
using FishLens_App.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FishLens_App
{
    public partial class Settings : Page
    {
        #region Fields
        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private UserSettings _config;
        #endregion

        #region Constructors
        public Settings(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            InitializeComponent();
            _config = (UserSettings)App.Settings;

            // Initialize UI from current state / persisted settings
            try
            {
                LoadSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
            }
        }
        #endregion

        #region Event Handlers
        private void ConfidenceThreshold_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (confidenceValue == null) return;
            var percent = (int)Math.Round(e.NewValue);
            confidenceValue.Text = $"{percent}%";
        }

        private void ToggleOutputMessages(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Not Implemented");
        }
        private void ToggleErrorMessages(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Not Implemented");
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Update configuration in memory
                _config.ConfidenceThreshold = (confidenceThreshold?.Value ?? 0) / 100.0;

                bool highContrast = highContrastMode.IsChecked ?? false;
                bool largeTxt = largeText.IsChecked ?? false;

                // Persist settings to a JSON file in project root
                // Update additional settings in shared config
                _config.HighContrastMode = highContrast;
                _config.LargeText = largeTxt;

                var settings = new UserSettings
                {
                    ConfidenceThreshold = _config.ConfidenceThreshold,
                    OutputBox = _config.OutputBox,
                    ErrorBox = _config.ErrorBox,
                    HighContrastMode = _config.HighContrastMode,
                    LargeText = _config.LargeText
                };

                string projectRoot = _pathResolver.ResolveProjectRoot();
                string configPath = System.IO.Path.Combine(projectRoot, "appsettings.json");
                var json = File.ReadAllText(configPath);
                var jsonDoc = JsonNode.Parse(json);

                jsonDoc["UserSettings"] = JsonSerializer.SerializeToNode(settings);

                var updatedJson = jsonDoc.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(configPath, updatedJson);
                App.Settings = settings;

                // Apply settings to main window
                ApplySettingsToMainWindow();

                MessageBox.Show("Settings updated.", "Success", MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Private Methods
        private void LoadSettings()
        {
            confidenceThreshold.Value = Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100);
            confidenceValue.Text = $"{(int)Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100)}%";
            hideErrors.IsChecked = _config.ErrorBox;
            hideOutput.IsChecked = _config.OutputBox;
            highContrastMode.IsChecked = _config.HighContrastMode;
            largeText.IsChecked = _config.LargeText;
        }

        private void ApplySettingsToMainWindow()
        {
            try
            {
                if (Application.Current.MainWindow != null)
                {
                    // Apply large text setting
                    if (_config.LargeText)
                    {
                        Application.Current.Resources["BaseFontSize"] = 18.0;
                        Application.Current.Resources["HeaderFontSize"] = 32.0;
                        Application.Current.Resources["LargeHeaderFontSize"] = 42.0;
                        Application.Current.Resources["TitleFontSize"] = 72.0;
                    }
                    else
                    {
                        Application.Current.Resources["BaseFontSize"] = 14.0;
                        Application.Current.Resources["HeaderFontSize"] = 24.0;
                        Application.Current.Resources["LargeHeaderFontSize"] = 32.0;
                        Application.Current.Resources["TitleFontSize"] = 56.0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply settings to main window");
                MessageBox.Show($"Failed to apply theme settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            ThemeHelper.ThemeSwap(_config.HighContrastMode);
        }
        #endregion
    }
}