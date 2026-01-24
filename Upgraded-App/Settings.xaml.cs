using FishLens_App.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : Page
    {
        private const int DEFAULT_CONFIDENCE_THRESHOLD_PERCENT = 70;
        private const int DEFAULT_FONT_SIZE = 14;
        private const int LARGE_FONT_SIZE = 18;
        private const string DEFAULT_BACKGROUND_COLOR = "#E8F4F8";
        private const string DEFAULT_FOREGROUND_COLOR = "#0D3640";
        private const string SETTINGS_FILE_NAME = "appsettings.json";
        private const string DEFAULT_VIDEO_QUALITY = "Medium";

        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private readonly CheckBoxToggle _checkBoxes;
        private readonly AppConfiguration _config;

        // **************************************************
        // Function: Constructor
        // Description: Initializes the Settings page with required dependencies and loads persisted settings
        // **************************************************
        public Settings(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));

            InitializeComponent();

            _checkBoxes = GetCheckBoxToggleFromApplication();
            _config = GetConfigurationFromApplication();

            TryLoadSettings();
        }

        // **************************************************
        // Function: ConfidenceThreshold_ValueChanged
        // Description: Updates the confidence threshold percentage display when slider value changes
        // **************************************************
        private void ConfidenceThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (confidenceValue == null)
                return;

            int percent = (int)Math.Round(e.NewValue);
            confidenceValue.Text = $"{percent}%";
        }

        // **************************************************
        // Function: Cancel_Click
        // Description: Handles cancel button click by navigating back or hiding the main frame
        // **************************************************
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CanNavigateBack())
                {
                    NavigateBack();
                    return;
                }

                HideMainFrame();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cancel navigation failed");
            }
        }

        // **************************************************
        // Function: SaveSettings_Click
        // Description: Saves all settings to configuration file and applies changes to the UI
        // **************************************************
        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateConfigurationFromUI();
                PersistSettingsToFile();
                ApplySettingsToMainWindow();

                ShowSaveSuccessMessage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                ShowSaveErrorMessage(ex.Message);
            }
        }

        // **************************************************
        // Function: LoadSettings
        // Description: Loads settings from configuration file and updates UI controls
        // **************************************************
        private void LoadSettings()
        {
            SetDefaultUIValues();

            string configPath = GetConfigurationFilePath();
            if (!File.Exists(configPath))
                return;

            try
            {
                ParseAndApplySettingsFromFile(configPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse settings file; using defaults");
            }
        }

        // **************************************************
        // Function: ToggleErrorMessages
        // Description: Toggles the visibility of error messages based on checkbox state
        // **************************************************
        private void ToggleErrorMessages(object sender, RoutedEventArgs e)
        {
            _checkBoxes.ErrorBox = hideErrors.IsChecked ?? false;
        }

        // **************************************************
        // Function: ToggleOutputMessages
        // Description: Toggles the visibility of output messages based on checkbox state
        // **************************************************
        private void ToggleOutputMessages(object sender, RoutedEventArgs e)
        {
            _checkBoxes.OutputBox = hideOutput.IsChecked ?? false;
        }

        #region Helper Methods

        // **************************************************
        // Function: GetCheckBoxToggleFromApplication
        // Description: Retrieves the CheckBoxToggle instance from the application
        // **************************************************
        private CheckBoxToggle GetCheckBoxToggleFromApplication()
        {
            return (Application.Current as App)?.CheckBoxes;
        }

        // **************************************************
        // Function: GetConfigurationFromApplication
        // Description: Retrieves the AppConfiguration instance from the application
        // **************************************************
        private AppConfiguration GetConfigurationFromApplication()
        {
            return (Application.Current as App)?.Configuration;
        }

        // **************************************************
        // Function: TryLoadSettings
        // Description: Attempts to load settings with error handling
        // **************************************************
        private void TryLoadSettings()
        {
            try
            {
                LoadSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
            }
        }

        // **************************************************
        // Function: CanNavigateBack
        // Description: Checks if navigation service can navigate back
        // **************************************************
        private bool CanNavigateBack()
        {
            return NavigationService != null && NavigationService.CanGoBack;
        }

        // **************************************************
        // Function: NavigateBack
        // Description: Navigates to the previous page
        // **************************************************
        private void NavigateBack()
        {
            NavigationService.GoBack();
        }

        // **************************************************
        // Function: HideMainFrame
        // Description: Hides the main frame of the application
        // **************************************************
        private void HideMainFrame()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainFrame.Visibility = Visibility.Collapsed;
            }
        }

        // **************************************************
        // Function: UpdateConfigurationFromUI
        // Description: Updates configuration objects with values from UI controls
        // **************************************************
        private void UpdateConfigurationFromUI()
        {
            _config.ConfidenceThreshold = GetConfidenceThresholdValue();

            _checkBoxes.ErrorBox = hideErrors.IsChecked ?? false;
            _checkBoxes.OutputBox = hideOutput.IsChecked ?? false;

            _config.AutoPlayVideos = autoPlayVideos.IsChecked ?? false;
            _config.VideoQuality = GetSelectedVideoQuality();
            _config.HighContrastMode = highContrastMode.IsChecked ?? false;
            _config.LargeText = largeText.IsChecked ?? false;
        }

        // **************************************************
        // Function: GetConfidenceThresholdValue
        // Description: Converts confidence threshold slider value to decimal (0-1 range)
        // **************************************************
        private double GetConfidenceThresholdValue()
        {
            return (confidenceThreshold?.Value ?? 0) / 100.0;
        }

        // **************************************************
        // Function: GetSelectedVideoQuality
        // Description: Retrieves the selected video quality from the combo box
        // **************************************************
        private string GetSelectedVideoQuality()
        {
            if (videoQuality?.SelectedItem is ComboBoxItem selected)
            {
                return selected.Content?.ToString() ?? DEFAULT_VIDEO_QUALITY;
            }
            return DEFAULT_VIDEO_QUALITY;
        }

        // **************************************************
        // Function: PersistSettingsToFile
        // Description: Serializes and saves settings to JSON configuration file
        // **************************************************
        private void PersistSettingsToFile()
        {
            var settingsObject = CreateSettingsObject();
            string configPath = GetConfigurationFilePath();

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settingsObject, options);
            File.WriteAllText(configPath, json);

            _logger.LogInformation("Settings saved to {path}", configPath);
        }

        // **************************************************
        // Function: CreateSettingsObject
        // Description: Creates an anonymous object containing all settings for serialization
        // **************************************************
        private object CreateSettingsObject()
        {
            return new
            {
                ConfidenceThreshold = _config.ConfidenceThreshold,
                OutputBox = _checkBoxes.OutputBox,
                ErrorBox = _checkBoxes.ErrorBox,
                AutoPlayVideos = _config.AutoPlayVideos,
                VideoQuality = _config.VideoQuality,
                HighContrastMode = _config.HighContrastMode,
                LargeText = _config.LargeText
            };
        }

        // **************************************************
        // Function: GetConfigurationFilePath
        // Description: Constructs the full path to the configuration file
        // **************************************************
        private string GetConfigurationFilePath()
        {
            string projectRoot = _pathResolver.ResolveProjectRoot();
            return Path.Combine(projectRoot ?? string.Empty, SETTINGS_FILE_NAME);
        }

        // **************************************************
        // Function: ApplySettingsToMainWindow
        // Description: Applies visual settings (contrast, font size) to the main window
        // **************************************************
        private void ApplySettingsToMainWindow()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow == null)
                    return;

                ApplyHighContrastSetting(mainWindow);
                ApplyLargeTextSetting(mainWindow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply settings to main window");
            }
        }

        // **************************************************
        // Function: ApplyHighContrastSetting
        // Description: Applies or removes high contrast mode styling to main window
        // **************************************************
        private void ApplyHighContrastSetting(MainWindow mainWindow)
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                if (_config.HighContrastMode)
                {
                    SetHighContrastColors(mainWindow);
                }
                else
                {
                    SetDefaultColors(mainWindow);
                }
            });
        }

        // **************************************************
        // Function: SetHighContrastColors
        // Description: Sets high contrast colors (black background, white text)
        // **************************************************
        private void SetHighContrastColors(MainWindow mainWindow)
        {
            mainWindow.Background = new SolidColorBrush(Colors.Black);

            var titleTextBlock = mainWindow.FindName("Title") as TextBlock;
            if (titleTextBlock != null)
            {
                titleTextBlock.Foreground = new SolidColorBrush(Colors.White);
            }
        }

        // **************************************************
        // Function: SetDefaultColors
        // Description: Sets default color scheme
        // **************************************************
        private void SetDefaultColors(MainWindow mainWindow)
        {
            var brushConverter = new BrushConverter();
            mainWindow.Background = (SolidColorBrush)brushConverter.ConvertFrom(DEFAULT_BACKGROUND_COLOR);

            var titleTextBlock = mainWindow.FindName("Title") as TextBlock;
            if (titleTextBlock != null)
            {
                titleTextBlock.Foreground = (SolidColorBrush)brushConverter.ConvertFrom(DEFAULT_FOREGROUND_COLOR);
            }
        }

        // **************************************************
        // Function: ApplyLargeTextSetting
        // Description: Applies or removes large text setting to main window
        // **************************************************
        private void ApplyLargeTextSetting(MainWindow mainWindow)
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                mainWindow.FontSize = _config.LargeText ? LARGE_FONT_SIZE : DEFAULT_FONT_SIZE;
            });
        }

        // **************************************************
        // Function: SetDefaultUIValues
        // Description: Sets default values for all UI controls from configuration
        // **************************************************
        private void SetDefaultUIValues()
        {
            double confidencePercent = (_config?.ConfidenceThreshold ?? 0.7) * 100;
            confidenceThreshold.Value = Math.Round(confidencePercent);
            confidenceValue.Text = $"{(int)Math.Round(confidencePercent)}%";

            hideErrors.IsChecked = _checkBoxes.ErrorBox;
            hideOutput.IsChecked = _checkBoxes.OutputBox;
        }

        // **************************************************
        // Function: ParseAndApplySettingsFromFile
        // Description: Reads and parses settings from JSON file, then applies to configuration and UI
        // **************************************************
        private void ParseAndApplySettingsFromFile(string configPath)
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            ApplyConfidenceThresholdFromJson(root);
            ApplyOutputBoxFromJson(root);
            ApplyErrorBoxFromJson(root);
            ApplyAutoPlayVideosFromJson(root);
            ApplyVideoQualityFromJson(root);
            ApplyHighContrastModeFromJson(root);
            ApplyLargeTextFromJson(root);
        }

        // **************************************************
        // Function: ApplyConfidenceThresholdFromJson
        // Description: Applies confidence threshold setting from JSON element
        // **************************************************
        private void ApplyConfidenceThresholdFromJson(JsonElement root)
        {
            if (root.TryGetProperty("ConfidenceThreshold", out var element) &&
                element.ValueKind == JsonValueKind.Number)
            {
                double confidence = element.GetDouble();
                _config.ConfidenceThreshold = confidence;
                confidenceThreshold.Value = Math.Round(confidence * 100);
                confidenceValue.Text = $"{(int)Math.Round(confidence * 100)}%";
            }
        }

        // **************************************************
        // Function: ApplyOutputBoxFromJson
        // Description: Applies output box visibility setting from JSON element
        // **************************************************
        private void ApplyOutputBoxFromJson(JsonElement root)
        {
            if (root.TryGetProperty("OutputBox", out var element) &&
                (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
            {
                _checkBoxes.OutputBox = element.GetBoolean();
                hideOutput.IsChecked = _checkBoxes.OutputBox;
            }
        }

        // **************************************************
        // Function: ApplyErrorBoxFromJson
        // Description: Applies error box visibility setting from JSON element
        // **************************************************
        private void ApplyErrorBoxFromJson(JsonElement root)
        {
            if (root.TryGetProperty("ErrorBox", out var element) &&
                (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
            {
                _checkBoxes.ErrorBox = element.GetBoolean();
                hideErrors.IsChecked = _checkBoxes.ErrorBox;
            }
        }

        // **************************************************
        // Function: ApplyAutoPlayVideosFromJson
        // Description: Applies auto-play videos setting from JSON element
        // **************************************************
        private void ApplyAutoPlayVideosFromJson(JsonElement root)
        {
            if (root.TryGetProperty("AutoPlayVideos", out var element) &&
                (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
            {
                _config.AutoPlayVideos = element.GetBoolean();
                autoPlayVideos.IsChecked = _config.AutoPlayVideos;
            }
        }

        // **************************************************
        // Function: ApplyVideoQualityFromJson
        // Description: Applies video quality setting from JSON element
        // **************************************************
        private void ApplyVideoQualityFromJson(JsonElement root)
        {
            if (root.TryGetProperty("VideoQuality", out var element) &&
                element.ValueKind == JsonValueKind.String)
            {
                string quality = element.GetString();
                SelectVideoQualityInComboBox(quality);
            }
        }

        // **************************************************
        // Function: SelectVideoQualityInComboBox
        // Description: Selects the matching video quality item in the combo box
        // **************************************************
        private void SelectVideoQualityInComboBox(string quality)
        {
            foreach (var item in videoQuality.Items)
            {
                if (item is ComboBoxItem comboItem && comboItem.Content?.ToString() == quality)
                {
                    videoQuality.SelectedItem = comboItem;
                    break;
                }
            }
        }

        // **************************************************
        // Function: ApplyHighContrastModeFromJson
        // Description: Applies high contrast mode setting from JSON element
        // **************************************************
        private void ApplyHighContrastModeFromJson(JsonElement root)
        {
            if (root.TryGetProperty("HighContrastMode", out var element) &&
                (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
            {
                _config.HighContrastMode = element.GetBoolean();
                highContrastMode.IsChecked = _config.HighContrastMode;
            }
        }

        // **************************************************
        // Function: ApplyLargeTextFromJson
        // Description: Applies large text setting from JSON element
        // **************************************************
        private void ApplyLargeTextFromJson(JsonElement root)
        {
            if (root.TryGetProperty("LargeText", out var element) &&
                (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
            {
                _config.LargeText = element.GetBoolean();
                largeText.IsChecked = _config.LargeText;
            }
        }

        // **************************************************
        // Function: ShowSaveSuccessMessage
        // Description: Displays a success message when settings are saved
        // **************************************************
        private void ShowSaveSuccessMessage()
        {
            MessageBox.Show("Settings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: ShowSaveErrorMessage
        // Description: Displays an error message when settings fail to save
        // **************************************************
        private void ShowSaveErrorMessage(string errorMessage)
        {
            MessageBox.Show($"Failed to save settings: {errorMessage}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion
    }
}