// **************************************************
// ***********************************
// File: MainWindow.xaml.cs
// Description: Handles the analysis page's functionality
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using FishLens_App.Interfaces;
using FishLens_App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FishLens_App
{
    public partial class MainWindow : Window
    {
        #region Constants

        private const double DEFAULT_CONFIDENCE_THRESHOLD = 0.7;
        private const int BUTTON_HEIGHT = 45;
        private const int BUTTON_FONT_SIZE = 13;
        private const int BUTTON_MARGIN = 5;
        private const int BUTTON_PADDING_HORIZONTAL = 12;
        private const int BUTTON_PADDING_VERTICAL = 8;
        private const int BUTTON_CORNER_RADIUS = 6;
        private const int CONTENT_PRESENTER_MARGIN = 8;
        private const string SAVED_VIDEOS_FOLDER = "SavedVids";
        private const string SAMPLE_DATA_FOLDER = "sample_data";

        #endregion

        #region Fields

        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private readonly AppConfiguration _config;
        private readonly CheckBoxToggle _checkBoxes;

        #endregion

        #region Constructors

        // **************************************************
        // Function: Constructor (Parameterized)
        // Description: Initializes MainWindow with dependency injection
        // **************************************************
        public MainWindow(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));

            InitializeComponent();

            _checkBoxes = GetCheckBoxToggleFromApplication();
            _config = GetConfigurationFromApplication();
        }

        // **************************************************
        // Function: Constructor (Default)
        // Description: Initializes MainWindow with default dependencies
        // **************************************************
        public MainWindow() : this(
            GetDefaultProjectPathResolver(),
            GetDefaultFileSystemManager(),
            GetDefaultLogger())
        {
        }

        #endregion

        #region Dependency Creation Helpers

        // **************************************************
        // Function: GetDefaultProjectPathResolver
        // Description: Creates default IProjectPathResolver instance
        // Notes: Used in parameterless constructor
        // **************************************************
        private static IProjectPathResolver GetDefaultProjectPathResolver()
        {
            return new DefaultProjectPathResolver();
        }

        // **************************************************
        // Function: GetDefaultFileSystemManager
        // Description: Creates default IFileSystemManager instance
        // Notes: Used in parameterless constructor
        // **************************************************
        private static IFileSystemManager GetDefaultFileSystemManager()
        {
            return new StandardFileSystemManager();
        }

        // **************************************************
        // Function: GetDefaultLogger
        // Description: Creates default ILogger<MainWindow> instance
        // Notes: Used in parameterless constructor
        // **************************************************
        private static ILogger<MainWindow> GetDefaultLogger()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
            });
            return loggerFactory.CreateLogger<MainWindow>();
        }

        // **************************************************
        // Function: GetCheckBoxToggleFromApplication
        // Description: Retrieves CheckBoxToggle instance from application
        // **************************************************
        private CheckBoxToggle GetCheckBoxToggleFromApplication()
        {
            return (Application.Current as App)?.CheckBoxes;
        }

        // **************************************************
        // Function: GetConfigurationFromApplication
        // Description: Retrieves AppConfiguration instance from application
        // **************************************************
        private AppConfiguration GetConfigurationFromApplication()
        {
            return (Application.Current as App)?.Configuration;
        }

        #endregion

        #region Path Resolution

        // **************************************************
        // Function: GetProjectRoot
        // Description: Returns the project root directory path
        // Notes: Implemented in DefaultProjectPathResolver.cs
        // **************************************************
        private string GetProjectRoot()
        {
            return _pathResolver.ResolveProjectRoot();
        }

        // **************************************************
        // Function: GetYoloScriptDirectory
        // Description: Returns the YOLO script file path
        // Notes: Implemented in DefaultProjectPathResolver.cs
        // **************************************************
        private string GetYoloScriptDirectory()
        {
            return _pathResolver.ResolveYoloScriptPath();
        }

        // **************************************************
        // Function: GetCsvScriptDirectory
        // Description: Returns the CSV script file path
        // Notes: Implemented in DefaultProjectPathResolver.cs
        // **************************************************
        private string GetCsvScriptDirectory()
        {
            return _pathResolver.ResolveCsvScriptDirectory();
        }

        // **************************************************
        // Function: GetSourceFolderPath
        // Description: Returns the user-selected source folder path
        // Notes: Implemented in DefaultProjectPathResolver.cs
        // **************************************************
        private string GetSourceFolderPath()
        {
            return _pathResolver.ResolveSourceFolder();
        }

        // **************************************************
        // Function: GetPath
        // Description: Resolves path for specified subdirectory
        // Notes: Implemented in DefaultProjectPathResolver.cs
        // **************************************************
        private string GetPath(string subdirectory)
        {
            return _pathResolver.ResolvePath(subdirectory);
        }

        #endregion

        #region Directory Management

        // **************************************************
        // Function: MakeDirectoryIfNotExists
        // Description: Creates directory if it doesn't already exist
        // **************************************************
        private void MakeDirectoryIfNotExists(string directory)
        {
            if (Directory.Exists(directory))
                return;

            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Permission denied creating directory");
                HandleDirectoryCreationError("Insufficient Permissions");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory");
                HandleDirectoryCreationError(ex.Message);
            }
        }

        // **************************************************
        // Function: HandleDirectoryCreationError
        // Description: Displays error message for directory creation failures
        // **************************************************
        private void HandleDirectoryCreationError(string errorMessage)
        {
            MessageBox.Show(
                $"Cannot create directory: {errorMessage}",
                "Directory Creation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        #endregion

        #region Page Navigation

        // **************************************************
        // Function: HomeButtonClick
        // Description: Navigates to the home page
        // **************************************************
        private void HomeButtonClick(object sender, RoutedEventArgs e)
        {
            MainFrame.Visibility = Visibility.Collapsed;
        }

        // **************************************************
        // Function: HistoryButtonClick
        // Description: Navigates to the history page
        // **************************************************
        private void HistoryButtonClick(object sender, RoutedEventArgs e)
        {
            MainFrame.Visibility = Visibility.Visible;
            _logger.LogInformation("History button clicked.");

            try
            {
                MainFrame.Navigate(new History(_pathResolver, _fileSystemManager, _logger));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Navigation Error: {ex.Message}");
            }
        }

        // **************************************************
        // Function: SettingsButtonClick
        // Description: Navigates to the settings page
        // **************************************************
        private void SettingsButtonClick(object sender, RoutedEventArgs e)
        {
            MainFrame.Visibility = Visibility.Visible;
            _logger.LogInformation("Settings button clicked.");

            try
            {
                MainFrame.Navigate(new Settings(_pathResolver, _fileSystemManager, _logger));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Navigation Error: {ex.Message}");
            }
        }

        #endregion

        #region YOLO Processing

        // **************************************************
        // Function: RunYolo
        // Description: Executes Python YOLO script for video analysis
        // Notes: Original writing credit to Aden Ratliff, async update by Benjamin Kerr
        // **************************************************
        private async Task RunYolo()
        {
            ProcessStartInfo processInfo = CreateYoloProcessStartInfo();

            ProgressDialog progressDialog = new ProgressDialog();
            progressDialog.Show();
            await Task.Delay(100);

            try
            {
                await ExecuteYoloProcess(processInfo, progressDialog);
            }
            catch (Exception ex)
            {
                progressDialog.Close();
                MessageBox.Show(ex.Message, "Could not process videos.", MessageBoxButton.OK);
            }
        }

        // **************************************************
        // Function: CreateYoloProcessStartInfo
        // Description: Configures process start information for YOLO script execution
        // **************************************************
        private ProcessStartInfo CreateYoloProcessStartInfo()
        {
            string yoloScriptDirectory = GetYoloScriptDirectory();
            string sampleDataPath = Path.Combine(GetProjectRoot(), SAMPLE_DATA_FOLDER);

            return new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{yoloScriptDirectory}\" \"{sampleDataPath}\"",
                RedirectStandardError = _checkBoxes.ErrorBox,
                RedirectStandardOutput = _checkBoxes.OutputBox,
                UseShellExecute = false
            };
        }

        // **************************************************
        // Function: ExecuteYoloProcess
        // Description: Runs YOLO process and handles output
        // **************************************************
        private async Task ExecuteYoloProcess(ProcessStartInfo processInfo, ProgressDialog progressDialog)
        {
            await Task.Run(() =>
            {
                using (Process process = Process.Start(processInfo))
                {
                    string output = ReadProcessOutput(process);
                    string error = ReadProcessError(process);

                    process.WaitForExit();

                    Dispatcher.Invoke(() => progressDialog.Close());

                    DisplayProcessOutputIfNeeded(output, error);
                }
            });
        }

        // **************************************************
        // Function: ReadProcessOutput
        // Description: Reads standard output from process if enabled
        // **************************************************
        private string ReadProcessOutput(Process process)
        {
            return _checkBoxes.OutputBox ? process.StandardOutput.ReadToEnd() : string.Empty;
        }

        // **************************************************
        // Function: ReadProcessError
        // Description: Reads standard error from process if enabled
        // **************************************************
        private string ReadProcessError(Process process)
        {
            return _checkBoxes.ErrorBox ? process.StandardError.ReadToEnd() : string.Empty;
        }

        // **************************************************
        // Function: DisplayProcessOutputIfNeeded
        // Description: Shows process output/errors if present
        // **************************************************
        private void DisplayProcessOutputIfNeeded(string output, string error)
        {
            if (!string.IsNullOrEmpty(error) || !string.IsNullOrEmpty(output))
            {
                Dispatcher.Invoke(() =>
                    MessageBox.Show($"Output:\n{output}\n\nErrors:\n{error}")
                );
            }
        }

        #endregion

        #region Video Processing

        // **************************************************
        // Function: OpenFolderClick
        // Description: Opens folder dialog and initiates video processing
        // **************************************************
        private void OpenFolderClick(object sender, RoutedEventArgs e)
        {
            string sourceFolderPath = GetSourceFolderPath();
            if (string.IsNullOrEmpty(sourceFolderPath))
                return;

            string saveDirectory = Path.Combine(GetProjectRoot(), SAVED_VIDEOS_FOLDER);
            ProcessVideos(sourceFolderPath, saveDirectory);

            exportData.Visibility = Visibility.Visible;
        }

        // **************************************************
        // Function: ProcessVideos
        // Description: Orchestrates complete video processing workflow
        // **************************************************
        private void ProcessVideos(string inputFolder, string outputDirectory)
        {
            MakeDirectoryIfNotExists(outputDirectory);
            DisplayDataInUi(outputDirectory);
            EnterDataInFile(inputFolder, outputDirectory);
            RunYolo();

            List<(FileInfo vid, Video data)> videoDataList = CreateSortedListOfVideos(outputDirectory);
            CreateVideoButtonsList(videoDataList);
        }

        // **************************************************
        // Function: EnterDataInFile
        // Description: Copies video files from input folder to output directory
        // **************************************************
        private void EnterDataInFile(string inputFolder, string outputDirectory)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(inputFolder);
            FileInfo[] files = dirInfo.GetFiles("*");

            foreach (FileInfo file in files)
            {
                CopyFileToDestination(file, outputDirectory);
            }
        }

        // **************************************************
        // Function: CopyFileToDestination
        // Description: Copies single file to destination with error handling
        // **************************************************
        private void CopyFileToDestination(FileInfo file, string outputDirectory)
        {
            string fileName = Path.GetFileName(file.FullName);
            string destinationPath = Path.Combine(outputDirectory, fileName);

            try
            {
                File.Copy(file.FullName, destinationPath, overwrite: true);
            }
            catch (IOException ex)
            {
                MessageBox.Show($"Error Saving File: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (SecurityException)
            {
                MessageBox.Show("Insufficient permissions to copy the file.", "Permission Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Video Data Management

        // **************************************************
        // Function: CreateSortedListOfVideos
        // Description: Creates list of videos sorted by confidence rating
        // **************************************************
        private List<(FileInfo vid, Video data)> CreateSortedListOfVideos(string directory)
        {
            DirectoryInfo vidsInfo = new DirectoryInfo(directory);
            FileInfo[] fileInfos = vidsInfo.GetFiles("*");

            List<(FileInfo vid, Video data)> videoDataList = new List<(FileInfo, Video)>();

            foreach (FileInfo vid in fileInfos)
            {
                if (IsVideoFile(vid))
                {
                    Video data = GetData(vid.Name);
                    videoDataList.Add((vid, data));
                }
            }

            return videoDataList.OrderBy(x => x.data.avgConfidence).ToList();
        }

        // **************************************************
        // Function: IsVideoFile
        // Description: Checks if file is a supported video format
        // **************************************************
        private bool IsVideoFile(FileInfo file)
        {
            string extension = file.Extension.ToLower();
            return extension == ".mp4" || extension == ".asf";
        }

        // **************************************************
        // Function: GetData
        // Description: Retrieves video analysis data from CSV file
        // **************************************************
        private Video GetData(string videoFileName)
        {
            Video vid = new Video();
            string csvPath = GetCsvScriptDirectory();

            if (!File.Exists(csvPath))
            {
                MessageBox.Show("Analysis data file not found.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return vid;
            }

            try
            {
                return GetVideoFileValues(vid, csvPath, videoFileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading analysis data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return vid;
            }
        }

        // **************************************************
        // Function: GetVideoFileValues
        // Description: Parses CSV row to populate Video object
        // Notes: Helper function for GetData
        // **************************************************
        private Video GetVideoFileValues(Video vid, string csvPath, string videoFileName)
        {
            string[] lines = File.ReadAllLines(csvPath);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(',');

                if (columns[0].Trim() == videoFileName)
                {
                    return PopulateVideoFromColumns(vid, columns);
                }
            }

            return CreateDefaultVideo(videoFileName);
        }

        // **************************************************
        // Function: PopulateVideoFromColumns
        // Description: Populates Video object from CSV columns
        // **************************************************
        private Video PopulateVideoFromColumns(Video vid, string[] columns)
        {
            vid.name = columns[0].Trim();
            vid.trackId = columns[1].Trim();
            vid.likelyClass = columns[2].Trim();
            vid.confidence = columns[3].Trim();
            vid.startTime = columns[4].Trim();
            vid.endTime = columns[5].Trim();
            vid.avgConfidence = double.Parse(columns[6].Trim());
            vid.direction = columns[7].Trim();

            return vid;
        }

        // **************************************************
        // Function: CreateDefaultVideo
        // Description: Creates Video object with default values when not found in CSV
        // **************************************************
        private Video CreateDefaultVideo(string videoFileName)
        {
            return new Video
            {
                name = videoFileName,
                trackId = "-1",
                likelyClass = "N/A",
                confidence = "00.00%",
                startTime = "00.00",
                endTime = "00.00",
                avgConfidence = 0.0,
                direction = "Unknown"
            };
        }

        #endregion

        #region Data Export

        // **************************************************
        // Function: ExportDataClick
        // Description: Exports analysis data to Excel file
        // **************************************************
        private void ExportDataClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string csvPath = GetCsvScriptDirectory();

                if (!File.Exists(csvPath))
                {
                    MessageBox.Show("No analysis data found to export.", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SaveFileDialog saveFileDialog = CreateExportSaveDialog();

                if (saveFileDialog.ShowDialog() == true)
                {
                    MakeExcelSheetAndInsertData(saveFileDialog, csvPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // **************************************************
        // Function: CreateExportSaveDialog
        // Description: Creates configured SaveFileDialog for Excel export
        // **************************************************
        private SaveFileDialog CreateExportSaveDialog()
        {
            return new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = $"FishLens_Analysis_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
            };
        }

        // **************************************************
        // Function: MakeExcelSheetAndInsertData
        // Description: Creates Excel workbook and populates with CSV data
        // Notes: Helper function for ExportDataClick
        // **************************************************
        private void MakeExcelSheetAndInsertData(SaveFileDialog saveFileDialog, string csvPath)
        {
            string excelPath = saveFileDialog.FileName;
            string[] allLines = File.ReadAllLines(csvPath);

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Analysis Data");

                WriteDataToWorksheet(worksheet, allLines);
                FormatWorksheet(worksheet, allLines);

                workbook.SaveAs(excelPath);
            }

            ShowExportSuccessMessage(excelPath);
            PromptToOpenExportedFile(excelPath);
        }

        // **************************************************
        // Function: WriteDataToWorksheet
        // Description: Writes CSV data to Excel worksheet
        // **************************************************
        private void WriteDataToWorksheet(ClosedXML.Excel.IXLWorksheet worksheet, string[] allLines)
        {
            for (int line = 0; line < allLines.Length; line++)
            {
                string[] columns = allLines[line].Split(',');
                for (int column = 0; column < columns.Length; column++)
                {
                    worksheet.Cell(line + 1, column + 1).Value = columns[column].Trim();
                }
            }
        }

        // **************************************************
        // Function: FormatWorksheet
        // Description: Applies formatting to Excel worksheet
        // **************************************************
        private void FormatWorksheet(ClosedXML.Excel.IXLWorksheet worksheet, string[] allLines)
        {
            if (allLines.Length > 0)
            {
                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
            }

            worksheet.Columns().AdjustToContents();
        }

        // **************************************************
        // Function: ShowExportSuccessMessage
        // Description: Displays success message after export
        // **************************************************
        private void ShowExportSuccessMessage(string excelPath)
        {
            MessageBox.Show($"Data exported successfully to:\n{excelPath}", "Export Successful",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: PromptToOpenExportedFile
        // Description: Asks user if they want to open the exported file
        // **************************************************
        private void PromptToOpenExportedFile(string excelPath)
        {
            var result = MessageBox.Show("Would you like to open the exported file?", "Open File",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(excelPath) { UseShellExecute = true });
            }
        }

        // **************************************************
        // Function: SaveButtonClick
        // Description: Saves user modifications to CSV file
        // **************************************************
        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentVideoName = videoName.Text;

                if (string.IsNullOrEmpty(currentVideoName) || currentVideoName == "--")
                {
                    MessageBox.Show("No video selected to save changes for.", "Save Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string csvPath = GetCsvScriptDirectory();

                if (!File.Exists(csvPath))
                {
                    MessageBox.Show("CSV file not found.", "Save Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                UpdateCsvFile(csvPath, currentVideoName);

                MessageBox.Show("Changes saved successfully!", "Save Successful",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving changes to CSV");
                MessageBox.Show($"Error saving changes: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // **************************************************
        // Function: UpdateCsvFile
        // Description: Updates CSV file with modified video data
        // **************************************************
        private void UpdateCsvFile(string csvPath, string videoFileName)
        {
            string[] lines = File.ReadAllLines(csvPath);
            List<string> updatedLines = new List<string>();

            // Keep the header
            updatedLines.Add(lines[0]);

            bool videoFound = false;

            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(',');

                // Check if this is the row for the current video
                if (columns[0].Trim() == videoFileName)
                {
                    // Update the row with modified values
                    string updatedRow = CreateUpdatedCsvRow(columns);
                    updatedLines.Add(updatedRow);
                    videoFound = true;
                }
                else
                {
                    // Keep the original row
                    updatedLines.Add(lines[i]);
                }
            }

            if (!videoFound)
            {
                throw new InvalidOperationException($"Video {videoFileName} not found in CSV file.");
            }

            // Write all lines back to the CSV file
            File.WriteAllLines(csvPath, updatedLines);
        }

        // **************************************************
        // Function: CreateUpdatedCsvRow
        // Description: Creates updated CSV row from UI values
        // **************************************************
        private string CreateUpdatedCsvRow(string[] originalColumns)
        {
            // Get values from UI controls
            string likelyClass = GetFishPresentClass();
            string direction = GetTravelDirectionValue();
            string species = fishSpecies.Text.Trim();

            // Keep original values for fields not editable in UI
            string videoFile = originalColumns[0].Trim();
            string trackId = originalColumns[1].Trim();
            string confidence = originalColumns[3].Trim();
            string startTime = originalColumns[4].Trim();
            string endTime = originalColumns[5].Trim();
            string avgConfidence = originalColumns[6].Trim();

            // If user entered a species, update the likely_class
            if (!string.IsNullOrEmpty(species) && species != "--")
            {
                likelyClass = species.ToLower();
            }

            // Build the CSV row
            return $"{videoFile},{trackId},{likelyClass},{confidence},{startTime},{endTime},{avgConfidence},{direction}";
        }

        // **************************************************
        // Function: GetFishPresentClass
        // Description: Converts UI fish present status to CSV class value
        // **************************************************
        private string GetFishPresentClass()
        {
            var selectedItem = fishPresentStatus.SelectedItem as ComboBoxItem;

            if (selectedItem == null)
            {
                return "unknown";
            }

            string status = selectedItem.Content.ToString();
            return status == "Present" ? "fish" : "not_fish";
        }

        // **************************************************
        // Function: GetTravelDirectionValue
        // Description: Gets travel direction value from UI
        // **************************************************
        private string GetTravelDirectionValue()
        {
            var selectedItem = travelDirection.SelectedItem as ComboBoxItem;

            if (selectedItem == null)
            {
                return "unknown";
            }

            return selectedItem.Content.ToString().ToLower();
        }

        #endregion

        #region UI Display

        // **************************************************
        // Function: VideoButtonClick
        // Description: Displays selected video and its data
        // **************************************************
        private void VideoButtonClick(object sender, RoutedEventArgs e)
        {
            Button clickedButton = (Button)sender;
            string videoPath = clickedButton.Tag.ToString();

            LoadVideoInPlayer(videoPath);

            string videoFileName = Path.GetFileName(videoPath);
            GetData(videoFileName);
        }

        // **************************************************
        // Function: LoadVideoInPlayer
        // Description: Loads video into media player with auto-play preference
        // **************************************************
        private void LoadVideoInPlayer(string videoPath)
        {
            videoPlayer.Source = new Uri(videoPath);

            if (_config?.AutoPlayVideos ?? true)
            {
                videoPlayer.Play();
            }
        }

        // **************************************************
        // Function: DisplayDataInUi
        // Description: Updates UI elements with video analysis data
        // **************************************************
        private void DisplayDataInUi(string videoFileName)
        {
            Video vid = GetData(videoFileName);

            videoName.Text = vid.name;
            videoDateTime.Text = $"Duration: {vid.startTime}s - {vid.endTime}s";
            fishPresentStatus.Text = vid.likelyClass == "fish" ? "Present" : "Not Present";
            fishPresentConfidence.Text = vid.avgConfidence.ToString();
            travelDirection.Text = CapitalizeFirstLetter(vid.direction);
        }

        // **************************************************
        // Function: CapitalizeFirstLetter
        // Description: Capitalizes the first letter of a string
        // **************************************************
        private string CapitalizeFirstLetter(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return char.ToUpper(text[0]) + text.Substring(1);
        }

        // **************************************************
        // Function: CreateVideoButtonsList
        // Description: Creates and adds buttons for all videos to sidebar
        // **************************************************
        private void CreateVideoButtonsList(List<(FileInfo videoFile, Video videoData)> videoDataList)
        {
            foreach (var (videoFile, videoData) in videoDataList)
            {
                Button button = CreateSingleVideoButton(videoFile, videoData);
                button.Click += VideoButtonClick;
                videoList.Children.Add(button);
            }
        }

        // **************************************************
        // Function: CreateSingleVideoButton
        // Description: Creates styled button for a single video
        // Notes: Helper function for CreateVideoButtonsList
        // **************************************************
        private Button CreateSingleVideoButton(FileInfo videoFile, Video videoData)
        {
            bool isLowConfidence = IsLowConfidence(videoData.avgConfidence);

            return new Button
            {
                Content = videoFile.Name,
                Margin = new Thickness(BUTTON_MARGIN),
                Padding = new Thickness(BUTTON_PADDING_HORIZONTAL, BUTTON_PADDING_VERTICAL,
                    BUTTON_PADDING_HORIZONTAL, BUTTON_PADDING_VERTICAL),
                Height = BUTTON_HEIGHT,
                Tag = videoFile.FullName,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = BUTTON_FONT_SIZE,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Style = CreateButtonStyle(isLowConfidence)
            };
        }

        // **************************************************
        // Function: IsLowConfidence
        // Description: Determines if confidence value is below threshold
        // **************************************************
        private bool IsLowConfidence(double confidence)
        {
            return confidence < (_config?.ConfidenceThreshold ?? DEFAULT_CONFIDENCE_THRESHOLD);
        }

        #endregion

        #region Button Styling

        // **************************************************
        // Function: CreateButtonStyle
        // Description: Creates styled button with hover effects and appropriate colors
        // **************************************************
        private Style CreateButtonStyle(bool isLowConfidence)
        {
            var style = new Style(typeof(Button));

            SetButtonDefaultAppearance(style, isLowConfidence);

            var template = CreateButtonControlTemplate(isLowConfidence);
            style.Setters.Add(new Setter(Button.TemplateProperty, template));

            return style;
        }

        // **************************************************
        // Function: SetButtonDefaultAppearance
        // Description: Sets default colors and properties for button
        // **************************************************
        private void SetButtonDefaultAppearance(Style style, bool isLowConfidence)
        {
            style.Setters.Add(new Setter(Button.BackgroundProperty,
                isLowConfidence
                    ? new SolidColorBrush(Color.FromRgb(254, 242, 242))
                    : new SolidColorBrush(Color.FromRgb(249, 250, 251))));

            style.Setters.Add(new Setter(Button.ForegroundProperty,
                isLowConfidence
                    ? new SolidColorBrush(Color.FromRgb(185, 28, 28))
                    : new SolidColorBrush(Color.FromRgb(55, 65, 81))));

            style.Setters.Add(new Setter(Button.BorderBrushProperty,
                new SolidColorBrush(Color.FromRgb(229, 231, 235))));
        }

        // **************************************************
        // Function: CreateButtonControlTemplate
        // Description: Creates control template with rounded corners and triggers
        // **************************************************
        private ControlTemplate CreateButtonControlTemplate(bool isLowConfidence)
        {
            var template = new ControlTemplate(typeof(Button));

            var border = CreateButtonBorder();
            template.VisualTree = border;

            AddButtonTriggers(template, isLowConfidence);

            return template;
        }

        // **************************************************
        // Function: CreateButtonBorder
        // Description: Creates border element for button template
        // **************************************************
        private FrameworkElementFactory CreateButtonBorder()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "border";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(BUTTON_CORNER_RADIUS));

            var contentPresenter = CreateContentPresenter();
            border.AppendChild(contentPresenter);

            return border;
        }

        // **************************************************
        // Function: CreateContentPresenter
        // Description: Creates content presenter for button template
        // **************************************************
        private FrameworkElementFactory CreateContentPresenter()
        {
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.MarginProperty,
                new Thickness(CONTENT_PRESENTER_MARGIN, 0, CONTENT_PRESENTER_MARGIN, 0));

            return contentPresenter;
        }

        // **************************************************
        // Function: AddButtonTriggers
        // Description: Adds hover and pressed triggers to button template
        // **************************************************
        private void AddButtonTriggers(ControlTemplate template, bool isLowConfidence)
        {
            var hoverTrigger = CreateHoverTrigger(isLowConfidence);
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = CreatePressedTrigger(isLowConfidence);
            template.Triggers.Add(pressedTrigger);
        }

        // **************************************************
        // Function: CreateHoverTrigger
        // Description: Creates mouse-over trigger for button
        // **************************************************
        private Trigger CreateHoverTrigger(bool isLowConfidence)
        {
            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };

            trigger.Setters.Add(new Setter(Button.BackgroundProperty,
                isLowConfidence
                    ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                    : new SolidColorBrush(Color.FromRgb(243, 244, 246)), "border"));

            trigger.Setters.Add(new Setter(Button.ForegroundProperty,
                isLowConfidence
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Color.FromRgb(17, 24, 39))));

            return trigger;
        }

        // **************************************************
        // Function: CreatePressedTrigger
        // Description: Creates button pressed trigger
        // **************************************************
        private Trigger CreatePressedTrigger(bool isLowConfidence)
        {
            var trigger = new Trigger { Property = Button.IsPressedProperty, Value = true };

            trigger.Setters.Add(new Setter(Button.BackgroundProperty,
                isLowConfidence
                    ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
                    : new SolidColorBrush(Color.FromRgb(229, 231, 235)), "border"));

            return trigger;
        }

        #endregion
    }
}