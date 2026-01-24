using FishLens_App.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : Page
    {
        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private CheckBoxToggle _checkBoxes;
        private AppConfiguration _config;

        public Settings(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            InitializeComponent();
            _checkBoxes = (Application.Current as App).CheckBoxes;
            _config = (Application.Current as App).Configuration;

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

        private void ConfidenceThreshold_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (confidenceValue == null) return;
            var percent = (int)Math.Round(e.NewValue);
            confidenceValue.Text = $"{percent}%";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Try to navigate back if possible, otherwise hide the main frame (return to home)
            try
            {
                if (this.NavigationService != null && this.NavigationService.CanGoBack)
                {
                    this.NavigationService.GoBack();
                    return;
                }

                var main = Application.Current.MainWindow as MainWindow;
                if (main != null)
                {
                    main.MainFrame.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cancel navigation failed");
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Update configuration in memory
                _config.ConfidenceThreshold = (confidenceThreshold?.Value ?? 0) / 100.0;

                // Ensure CheckBoxToggle is up to date
                _checkBoxes.ErrorBox = hideErrors.IsChecked ?? false;
                _checkBoxes.OutputBox = hideOutput.IsChecked ?? false;

                // Video and accessibility options
                bool autoplay = autoPlayVideos.IsChecked ?? false;
                string quality = "Medium";
                if (videoQuality?.SelectedItem is ComboBoxItem selected)
                {
                    quality = selected.Content?.ToString() ?? quality;
                }
                bool highContrast = highContrastMode.IsChecked ?? false;
                bool largeTxt = largeText.IsChecked ?? false;

                // Persist settings to a JSON file in project root
                // Update additional settings in shared config
                _config.AutoPlayVideos = autoplay;
                _config.VideoQuality = quality;
                _config.HighContrastMode = highContrast;
                _config.LargeText = largeTxt;

                var settingsObj = new
                {
                    ConfidenceThreshold = _config.ConfidenceThreshold,
                    OutputBox = _checkBoxes.OutputBox,
                    ErrorBox = _checkBoxes.ErrorBox,
                    AutoPlayVideos = _config.AutoPlayVideos,
                    VideoQuality = _config.VideoQuality,
                    HighContrastMode = _config.HighContrastMode,
                    LargeText = _config.LargeText
                };

                string projectRoot = _pathResolver.ResolveProjectRoot();
                string configPath = System.IO.Path.Combine(projectRoot ?? string.Empty, "appsettings.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, JsonSerializer.Serialize(settingsObj, options));

                MessageBox.Show("Settings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                _logger.LogInformation("Settings saved to {path}", configPath);
                // Apply certain settings immediately to main window
                try
                {
                    var main = Application.Current.MainWindow as MainWindow;
                    if (main != null)
                    {
                        // High contrast
                        if (_config.HighContrastMode)
                        {
                            main.Dispatcher.Invoke(() =>
                            {
                                main.Background = new SolidColorBrush(Colors.Black);
                                var titleTb = main.FindName("Title") as TextBlock;
                                if (titleTb != null) titleTb.Foreground = new SolidColorBrush(Colors.White);
                            });
                        }
                        else
                        {
                            main.Dispatcher.Invoke(() =>
                            {
                                main.Background = (SolidColorBrush)(new BrushConverter().ConvertFrom("#E8F4F8"));
                                var titleTb = main.FindName("Title") as TextBlock;
                                if (titleTb != null) titleTb.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                            });
                        }

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
                catch { }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSettings()
        {
            // Set defaults from current runtime state
            confidenceThreshold.Value = Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100);
            confidenceValue.Text = $"{(int)Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100)}%";
            hideErrors.IsChecked = _checkBoxes.ErrorBox;
            hideOutput.IsChecked = _checkBoxes.OutputBox;

            // Try to read persisted settings
            try
            {
                string projectRoot = _pathResolver.ResolveProjectRoot();
                string configPath = System.IO.Path.Combine(projectRoot ?? string.Empty, "appsettings.json");
                if (!File.Exists(configPath)) return;

                using var stream = File.OpenRead(configPath);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("ConfidenceThreshold", out var confEl) && confEl.ValueKind == JsonValueKind.Number)
                {
                    double conf = confEl.GetDouble();
                    _config.ConfidenceThreshold = conf;
                    confidenceThreshold.Value = Math.Round(conf * 100);
                    confidenceValue.Text = $"{(int)Math.Round(conf * 100)}%";
                }

                if (root.TryGetProperty("OutputBox", out var outEl) && outEl.ValueKind == JsonValueKind.True || outEl.ValueKind == JsonValueKind.False)
                {
                    _checkBoxes.OutputBox = outEl.GetBoolean();
                    hideOutput.IsChecked = _checkBoxes.OutputBox;
                }

                if (root.TryGetProperty("ErrorBox", out var errEl) && errEl.ValueKind == JsonValueKind.True || errEl.ValueKind == JsonValueKind.False)
                {
                    _checkBoxes.ErrorBox = errEl.GetBoolean();
                    hideErrors.IsChecked = _checkBoxes.ErrorBox;
                }

                if (root.TryGetProperty("AutoPlayVideos", out var autoEl) && (autoEl.ValueKind == JsonValueKind.True || autoEl.ValueKind == JsonValueKind.False))
                {
                    _config.AutoPlayVideos = autoEl.GetBoolean();
                    autoPlayVideos.IsChecked = _config.AutoPlayVideos;
                }

                if (root.TryGetProperty("VideoQuality", out var qualityEl) && qualityEl.ValueKind == JsonValueKind.String)
                {
                    string q = qualityEl.GetString();
                    foreach (var item in videoQuality.Items)
                    {
                        if (item is ComboBoxItem c && c.Content?.ToString() == q)
                        {
                            videoQuality.SelectedItem = c;
                            break;
                        }
                    }
                }

                if (root.TryGetProperty("HighContrastMode", out var hcEl) && (hcEl.ValueKind == JsonValueKind.True || hcEl.ValueKind == JsonValueKind.False))
                {
                    _config.HighContrastMode = hcEl.GetBoolean();
                    highContrastMode.IsChecked = _config.HighContrastMode;
                }

                if (root.TryGetProperty("LargeText", out var ltEl) && (ltEl.ValueKind == JsonValueKind.True || ltEl.ValueKind == JsonValueKind.False))
                {
                    _config.LargeText = ltEl.GetBoolean();
                    largeText.IsChecked = _config.LargeText;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse settings file; using defaults");
            }
        }

        private void ToggleErrorMessages(object sender, RoutedEventArgs e)
        {
            _checkBoxes.ErrorBox = hideErrors.IsChecked ?? false;
        }

        private void ToggleOutputMessages(object sender, RoutedEventArgs e)
        {
            _checkBoxes.OutputBox = hideOutput.IsChecked ?? false;
        }

    }
}
