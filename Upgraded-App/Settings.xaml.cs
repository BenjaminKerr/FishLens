using DocumentFormat.OpenXml.Spreadsheet;
using FishLens_App;
using FishLens_App.Interfaces;
using FishLens_App.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
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
    public partial class Settings : System.Windows.Controls.Page
    {
        #region Fields
       
        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private CheckBoxToggle _checkBoxes;
        private AppConfiguration _config;



        #endregion

        #region Constructors
        public Settings(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            InitializeComponent();
            
            LoadAll();
        }
        #endregion

        #region Event Handlers

        // ****************************************************************
        // Function: ScrollViewer_PreviewMouseWheel
        // Description: Slows down and smooths scrolling speed
        // Notes: Reduces scroll delta to make scrolling less snappy
        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                // Scroll slower - reduce delta by 75% (multiply by 0.25)
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.25));
                e.Handled = true;
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
                var app = Application.Current as App;
                int userId = app.CurrentUserId;

                // Update in-memory state from the UI
                _config.ConfidenceThreshold = (confidenceThreshold?.Value ?? 0) / 100.0;
                _checkBoxes.ErrorBox = hideErrors.IsChecked ?? false;
                _checkBoxes.OutputBox = hideOutput.IsChecked ?? false;
                _config.HighContrastMode = highContrastMode.IsChecked ?? false;
                _config.LargeText = largeText.IsChecked ?? false;

                // Persist to database
                using (SqlConnection conn = new SqlConnection(app.connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("kaharra.SaveUserSettings", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@pUserId", userId);
                        cmd.Parameters.AddWithValue("@pConfidenceThreshold", _config.ConfidenceThreshold);
                        cmd.Parameters.AddWithValue("@pOutputBox", _checkBoxes.OutputBox);
                        cmd.Parameters.AddWithValue("@pErrorBox", _checkBoxes.ErrorBox);
                        cmd.Parameters.AddWithValue("@pHighContrastMode", _config.HighContrastMode);
                        cmd.Parameters.AddWithValue("@pLargeText", _config.LargeText);
                        cmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("Settings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                _logger.LogInformation("Settings saved to database for user {userId}", userId);

                try
                {
                    app.ApplyCurrentSettings();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply settings to application resources");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings to database");
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        #endregion

        #region Private Methods

       

        // ****************************************************************
        // Function: LoadAll
        // Description: Function to cleanup code Initializes UI from current state / persisted settings / database values
        // Notes: N/A
        private void LoadAll()
        { 
            try
            {
                DataContext = this;
                _checkBoxes = (Application.Current as App).CheckBoxes;
                _config = (Application.Current as App).Configuration;
              
                LoadSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
            }
        }




        private void LoadSettings()
        {
            var app = Application.Current as App;
            int userId = app.CurrentUserId;

            // Apply runtime-default fallbacks first, in case the DB has no row yet
            confidenceThreshold.Value = Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100);
            confidenceValue.Text = $"{(int)Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100)}%";
            hideErrors.IsChecked = _checkBoxes.ErrorBox;
            hideOutput.IsChecked = _checkBoxes.OutputBox;

            try
            {
                using (SqlConnection conn = new SqlConnection(app.connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("kaharra.GetUserSettings", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@pUserId", userId);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                double conf = reader.GetDouble(0);
                                bool outputBox = reader.GetBoolean(1);
                                bool errorBox = reader.GetBoolean(2);
                                bool highContrast = reader.GetBoolean(3);
                                bool largeTxt = reader.GetBoolean(4);

                                _config.ConfidenceThreshold = conf;
                                _checkBoxes.OutputBox = outputBox;
                                _checkBoxes.ErrorBox = errorBox;
                                _config.HighContrastMode = highContrast;
                                _config.LargeText = largeTxt;

                                confidenceThreshold.Value = Math.Round(conf * 100);
                                confidenceValue.Text = $"{(int)Math.Round(conf * 100)}%";
                                hideOutput.IsChecked = outputBox;
                                hideErrors.IsChecked = errorBox;
                                highContrastMode.IsChecked = highContrast;
                                largeText.IsChecked = largeTxt;
                            }
                            // If no row exists, the defaults set above will be used.
                            // First save will INSERT via the MERGE in SaveUserSettings.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load settings from database; using defaults");
            }
        }




       
        




        #endregion


    }
}