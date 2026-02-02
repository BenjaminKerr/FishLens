// **************************************************
// ***********************************
// File: MainWindow.xaml.cs
// Description: Handles the analysis page's functionality
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using FishLens_App.Interfaces;
using FishLens_App.Models;
using FishLens_App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        // Application settings
        private const double DEFAULT_CONFIDENCE_THRESHOLD = 0.7;

        // UI constants - Video buttons
        private const int BUTTON_HEIGHT = 45;
        private const int BUTTON_FONT_SIZE = 13;
        private const int BUTTON_MARGIN = 5;
        private const int BUTTON_PADDING_HORIZONTAL = 12;
        private const int BUTTON_PADDING_VERTICAL = 8;
        private const int BUTTON_CORNER_RADIUS = 6;
        private const int CONTENT_PRESENTER_MARGIN = 8;

        // Directory paths
        private const string SAVED_VIDEOS_FOLDER = "SavedVids";
        private const string SAMPLE_DATA_FOLDER = "sample_data";
        private const string TRASH_FOLDER = ".trash";

        // State
        private string _currentFolderName = string.Empty;

        #endregion

        #region Fields

        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private readonly AppConfiguration _config;
        private readonly CheckBoxToggle _checkBoxes;
        // Stack of deletion batches for undo support
        private readonly Stack<DeletionBatch> _deletionHistory = new Stack<DeletionBatch>();

        #endregion

        #region Nested Classes

        // **************************************************
        // Type: DeletionBatch
        // Description: Tracks moved files and CSV rows for undo
        // **************************************************
        private class DeletionBatch
        {
            public string TrashFolder { get; set; }
            public List<(string originalPath, string trashPath, string csvRow, FishLens_App.Models.Video video, string folder)> Items { get; } = new List<(string, string, string, FishLens_App.Models.Video, string)>();
        }

        #endregion

        #region Constructors

        // **************************************************
        // Function: Constructor (Parameterized)
        // Description: Initializes MainWindow with dependency injection
        // **************************************************
        public MainWindow(IProjectPathResolver pathResolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));

            InitializeComponent();

            _checkBoxes = GetCheckBoxToggleFromApplication();
            _config = GetConfigurationFromApplication();

            // Only reset theme if high contrast mode was enabled
            Loaded += (s, e) =>
            {
                if (_config?.HighContrastMode ?? false)
                {
                    // Reset to normal mode
                    _config.HighContrastMode = false;
                    ThemeHelper.ApplyHighContrastMode(false);
                }
                // Otherwise, leave everything at default XAML values
            };
        }

        // **************************************************
        // Function: Constructor (Default)
        // Description: Initializes MainWindow with default dependencies
        // **************************************************
        public MainWindow() : this(
            GetDefaultProjectPathResolver(),
            GetDefaultFileSystemManager(),
            NullLogger<MainWindow>.Instance)
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
            ExpandSidebar();
            MainFrame.Visibility = Visibility.Collapsed;
        }

        // **************************************************
        // Function: HistoryButtonClick
        // Description: Navigates to the history page
        // **************************************************
        private void HistoryButtonClick(object sender, RoutedEventArgs e)
        {
            CollapseSidebar();
            NavigateToPage(new History(_pathResolver, _fileSystemManager, _logger), "History");
        }

        // **************************************************
        // Function: SettingsButtonClick
        // Description: Navigates to the settings page
        // **************************************************
        private void SettingsButtonClick(object sender, RoutedEventArgs e)
        {
            CollapseSidebar();
            NavigateToPage(new Settings(_pathResolver, _fileSystemManager, _logger), "Settings");
        }

        // **************************************************
        // Function: NavigateToPage
        // Description: Handles logic common to both navigation functions
        // **************************************************
        private void NavigateToPage(object page, string pageName)
        {
            MainFrame.Visibility = Visibility.Visible;
            _logger.LogInformation("Navigating to {PageName}", pageName);

            try
            {
                MainFrame.Navigate(page);

                // Apply high contrast theme if enabled
                if (_config?.HighContrastMode ?? false)
                {
                    MainFrame.Dispatcher.InvokeAsync(
                        () => ThemeHelper.ApplyHighContrastMode(true),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to navigate to {PageName}", pageName);
                MessageBox.Show(
                    $"Navigation Error: {ex.Message}",
                    "Navigation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

            #region YOLO Processing

            // **************************************************
            // Function: RunYolo
            // Description: Executes Python YOLO script for video analysis
            // Notes: Original writing credit to Aden Ratliff, async update by Benjamin Kerr
            //          Running async so that UI thread isn't blocked
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
            string yoloScriptDirectory = _pathResolver.ResolveYoloScriptPath();
            string sampleDataPath = Path.Combine(_pathResolver.ResolveProjectRoot(), SAMPLE_DATA_FOLDER);

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
            string sourceFolderPath = _pathResolver.ResolveSourceFolder();
            _currentFolderName = Path.GetFileName(sourceFolderPath);
            if (string.IsNullOrEmpty(sourceFolderPath))
                return;

            string saveDirectory = Path.Combine(_pathResolver.ResolveProjectRoot(), SAVED_VIDEOS_FOLDER);
            ProcessVideos(sourceFolderPath, saveDirectory);

            exportData.Visibility = Visibility.Visible;
        }

        // **************************************************
        // Function: ProcessVideos
        // Description: Orchestrates complete video processing workflow
        // **************************************************
        private async void ProcessVideos(string inputFolder, string outputDirectory)
        {
            MakeDirectoryIfNotExists(outputDirectory);
            DisplayDataInUi(outputDirectory);
            EnterDataInFile(inputFolder, outputDirectory);
            await RunYolo();

            List<(FileInfo vid, FishLens_App.Models.Video data)> videoDataList = CreateSortedListOfVideos(outputDirectory);
            CreateVideoButtonsList(videoDataList);

            // If configured, load (and auto-play) the first uploaded video
            try
            {
                if (videoDataList != null && videoDataList.Count > 0)
                {
                    var firstVideoPath = videoDataList[0].vid.FullName;
                    // Load the video into the player. LoadVideoInPlayer will respect _config.AutoPlayVideos
                    Dispatcher.Invoke(() => LoadVideoInPlayer(firstVideoPath));
                }
            }
            catch { }
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

        public void DeleteSelectedVideosClick(object sender, EventArgs e)
        {
            var selected = GetSelectedVideoGrids();
            if (selected.Count == 0)
            {
                MessageBox.Show("No videos selected for deletion.", "Delete Videos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!ConfirmDelete(selected.Count)) return;

            string csvPath = _pathResolver.ResolveCsvScriptPath();

            DeleteFilesAndCsvEntries(selected.Select(x => x.path).ToList(), csvPath);

            RemoveUiGrids(selected.Select(x => x.grid).ToList());

            MessageBox.Show($"Deleted {selected.Count} video(s).", "Delete Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: GetSelectedVideoGrids
        // Description: Returns list of selected video grids and their file paths
        // **************************************************
        private List<(Grid grid, string path)> GetSelectedVideoGrids()
        {
            var result = new List<(Grid, string)>();
            foreach (var child in videoList.Children)
            {
                if (child is Grid g)
                {
                    CheckBox cb = null;
                    Button btn = null;
                    foreach (var elem in g.Children)
                    {
                        if (elem is CheckBox c) cb = c;
                        if (elem is Button b) btn = b;
                    }

                    if (cb != null && cb.IsChecked == true && btn != null && btn.Tag is string path)
                    {
                        result.Add((g, path));
                    }
                }
            }

            return result;
        }

        // **************************************************
        // Function: ConfirmDelete
        // Description: Confirms deletion with the user
        // **************************************************
        private bool ConfirmDelete(int count)
        {
            var result = MessageBox.Show($"Delete {count} selected video(s) and remove their analysis?","Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }

        // **************************************************
        // Function: DeleteFilesAndCsvEntries
        // Description: Deletes files and removes their CSV rows
        // **************************************************
        private void DeleteFilesAndCsvEntries(List<string> paths, string csvPath)
        {
            // Create a trash folder to hold deleted files for potential undo
            string trashRoot = Path.Combine(_pathResolver.ResolvePath(SAVED_VIDEOS_FOLDER), ".trash");
            Directory.CreateDirectory(trashRoot);
            var batch = new DeletionBatch { TrashFolder = Path.Combine(trashRoot, Guid.NewGuid().ToString()) };
            Directory.CreateDirectory(batch.TrashFolder);

            for (int i = 0; i < paths.Count; i++)
            {
                string fullPath = paths[i];
                try
                {
                    // capture video data and CSV row before removal
                    string fileName = Path.GetFileName(fullPath);
                    string csvRow = null;
                    if (File.Exists(csvPath))
                    {
                        var all = File.ReadAllLines(csvPath);
                        for (int r = 1; r < all.Length; r++)
                        {
                            var cols = all[r].Split(',');
                            if (cols.Length > 0 && string.Equals(cols[0].Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                            {
                                csvRow = all[r];
                                break;
                            }
                        }
                    }

                    var videoData = FishLens_App.Services.CsvUtils.ReadVideoFromCsv(csvPath, fileName);

                    if (videoPlayer.Source != null && string.Equals(videoPlayer.Source.LocalPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        videoPlayer.Stop();
                        videoPlayer.Source = null;
                    }

                    string trashPath = Path.Combine(batch.TrashFolder, Path.GetFileName(fullPath));
                    if (File.Exists(fullPath))
                    {
                        File.Move(fullPath, trashPath, overwrite: true);
                    }

                    batch.Items.Add((fullPath, trashPath, csvRow, videoData, GetFolderTagForPath(fullPath)));

                    // remove CSV entry
                    if (File.Exists(csvPath) && csvRow != null)
                    {
                        FishLens_App.Services.CsvUtils.RemoveVideoFromCsv(csvPath, fileName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete/move file {path}", fullPath);
                }
            }

            // push batch for undo
            if (batch.Items.Count > 0)
            {
                _deletionHistory.Push(batch);
            }
        }

        // **************************************************
        // Function: RemoveUiGrids
        // Description: Removes video grids and cleans up empty folder headers/separators
        // **************************************************
        private void RemoveUiGrids(List<Grid> grids)
        {
            foreach (var g in grids)
            {
                videoList.Children.Remove(g);
            }

            var headersToRemove = new List<UIElement>();
            foreach (var child in videoList.Children)
            {
                if (child is Grid headerGrid && headerGrid.Tag is string t && t.StartsWith("header:"))
                {
                    string folder = t.Substring("header:".Length);
                    bool anyRemaining = false;
                    foreach (var c2 in videoList.Children)
                    {
                        if (c2 is Grid g2 && g2.Tag is string t2 && t2 == folder)
                        {
                            anyRemaining = true;
                            break;
                        }
                    }

                    if (!anyRemaining) headersToRemove.Add(headerGrid);
                }
            }

            foreach (var h in headersToRemove)
            {
                int idx = videoList.Children.IndexOf(h);
                if (idx >= 0)
                {
                    videoList.Children.RemoveAt(idx);
                    if (videoList.Children.Count > idx && videoList.Children[idx] is Separator)
                    {
                        videoList.Children.RemoveAt(idx);
                    }
                }
            }
        }

        // **************************************************
        // Function: GetFolderTagForPath
        // Description: Derives the folder tag (folder name) for a given file path
        // **************************************************
        private string GetFolderTagForPath(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return string.Empty;
                return Path.GetFileName(dir);
            }
            catch { return string.Empty; }
        }

        // **************************************************
        // Function: UndoLastDeleteClick
        // Description: UI handler to undo the most recent deletion batch
        // **************************************************
        public void UndoLastDeleteClick(object sender, EventArgs e)
        {
            if (_deletionHistory.Count == 0)
            {
                MessageBox.Show("Nothing to undo.", "Undo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var batch = _deletionHistory.Pop();
            // restore files
            var restoredFiles = new List<string>();
            var rowsToRestore = new List<string>();

            foreach (var item in batch.Items)
            {
                try
                {
                    if (File.Exists(item.trashPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(item.originalPath) ?? _pathResolver.ResolvePath(SAVED_VIDEOS_FOLDER));
                        File.Move(item.trashPath, item.originalPath, overwrite: true);
                    }
                    restoredFiles.Add(item.originalPath);
                    if (!string.IsNullOrEmpty(item.csvRow)) rowsToRestore.Add(item.csvRow);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to restore file {path}", item.originalPath);
                }
            }

            // restore CSV rows
            try
            {
                if (rowsToRestore.Count > 0)
                {
                    FishLens_App.Services.CsvUtils.AppendRows(_pathResolver.ResolveCsvScriptPath(), rowsToRestore);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore CSV rows");
            }

            // restore UI
            RestoreUiForFiles(restoredFiles, batch);

            // cleanup trash folder
            try { if (Directory.Exists(batch.TrashFolder)) Directory.Delete(batch.TrashFolder, recursive: true); } catch { }

            MessageBox.Show($"Restored {restoredFiles.Count} file(s).", "Undo Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: RestoreUiForFiles
        // Description: Recreates UI entries for restored files using saved video data
        // **************************************************
        private void RestoreUiForFiles(List<string> restoredFiles, DeletionBatch batch)
        {
            foreach (var filePath in restoredFiles)
            {
                // find corresponding item in batch
                var item = batch.Items.Find(x => x.originalPath == filePath);
                if (item.originalPath == null) continue;

                // ensure folder header exists
                bool headerExists = false;
                foreach (var child in videoList.Children)
                {
                    if (child is Grid g && g.Tag is string t && t == $"header:{item.folder}") { headerExists = true; break; }
                }
                if (!headerExists)
                {
                    // temporarily set folderName so CreateFolderHeader uses it
                    var prev = _currentFolderName;
                    _currentFolderName = item.folder;
                    CreateFolderHeader();
                    _currentFolderName = prev;
                }

                // create video grid and add
                Grid grid = new Grid();
                grid.Tag = item.folder;
                grid.HorizontalAlignment = HorizontalAlignment.Stretch;
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                FileInfo fi = new FileInfo(filePath);
                Button button = CreateSingleVideoButton(fi, item.video);
                button.Click += VideoButtonClick;
                Grid.SetColumn(button, 0);

                CheckBox checkBox = new CheckBox();
                checkBox.Padding = new Thickness(5);
                checkBox.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(checkBox, 1);

                grid.Children.Add(button);
                grid.Children.Add(checkBox);
                videoList.Children.Add(grid);
            }
        }

        // **************************************************
        // Function: CreateSortedListOfVideos
        // Description: Creates list of videos sorted by confidence rating
        // **************************************************
        private List<(FileInfo vid, FishLens_App.Models.Video data)> CreateSortedListOfVideos(string directory)
        {
            DirectoryInfo vidsInfo = new DirectoryInfo(directory);
            FileInfo[] fileInfos = vidsInfo.GetFiles("*");

            List<(FileInfo vid, FishLens_App.Models.Video data)> videoDataList = new List<(FileInfo, FishLens_App.Models.Video)>();

            foreach (FileInfo vid in fileInfos)
            {
                if (IsVideoFile(vid))
                {
                    FishLens_App.Models.Video data = GetData(vid.Name);
                    videoDataList.Add((vid, data));
                }
            }

            return videoDataList.OrderBy(x => x.data.AvgConfidence).ToList();
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
        private FishLens_App.Models.Video GetData(string videoFileName)
        {
            FishLens_App.Models.Video vid = new FishLens_App.Models.Video();
            string csvPath = _pathResolver.ResolveCsvScriptPath();

            if (!File.Exists(csvPath))
            {
                MessageBox.Show("Analysis data file not found.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return vid;
            }

            try
            {
                return FishLens_App.Services.CsvUtils.ReadVideoFromCsv(csvPath, videoFileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading analysis data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return vid;
            }
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
                string csvPath = _pathResolver.ResolveCsvScriptPath();

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

                string csvPath = _pathResolver.ResolveCsvScriptPath();

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
            // Create updated row using current UI values
            string[] lines = File.ReadAllLines(csvPath);
            string[] columns = null;
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length > 0 && string.Equals(cols[0].Trim(), videoFileName, StringComparison.OrdinalIgnoreCase))
                {
                    columns = cols;
                    break;
                }
            }

            if (columns == null)
                throw new InvalidOperationException($"Video {videoFileName} not found in CSV file.");

            string updatedRow = CreateUpdatedCsvRow(columns);
            FishLens_App.Services.CsvUtils.UpdateCsvRow(csvPath, videoFileName, updatedRow);
        }

        // CSV removal moved to CsvUtils for reuse and testability

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

        private void CollapseSidebar()
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 106,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new System.Windows.Media.Animation.CubicEase()
            };

            SideBar.BeginAnimation(Border.WidthProperty, animation);

            videoList.Visibility = Visibility.Collapsed;
            deleteSelectedVideos.Visibility = Visibility.Collapsed;
            undoLastDelete.Visibility = Visibility.Collapsed;
            sidebarSeperator.Visibility = Visibility.Collapsed;
            videoLibraryTitle.Visibility = Visibility.Collapsed;

            ButtonGrid.RowDefinitions.Clear();
            ButtonGrid.ColumnDefinitions.Clear();
            ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(Home, 0);
            Grid.SetColumn(Home, 0);
            Grid.SetRow(History, 1);
            Grid.SetColumn(History, 0);
            Grid.SetRow(Settings, 2);
            Grid.SetColumn(Settings, 0);
        }

        private void ExpandSidebar()
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 320,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new System.Windows.Media.Animation.CubicEase()
            };

            SideBar.BeginAnimation(Border.WidthProperty, animation);

            // Show video list
            videoList.Visibility = Visibility.Visible;
            deleteSelectedVideos.Visibility = Visibility.Visible;
            undoLastDelete.Visibility = Visibility.Visible;
            sidebarSeperator.Visibility = Visibility.Visible;
            videoLibraryTitle.Visibility = Visibility.Visible;

            // Restore horizontal button layout
            ButtonGrid.RowDefinitions.Clear();
            ButtonGrid.ColumnDefinitions.Clear();
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(Home, 0);
            Grid.SetColumn(Home, 0);
            Grid.SetRow(History, 0);
            Grid.SetColumn(History, 1);
            Grid.SetRow(Settings, 0);
            Grid.SetColumn(Settings, 2);
        }

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
            videoPlayer.Play();
        }

        // **************************************************
        // Function: DisplayDataInUi
        // Description: Updates UI elements with video analysis data
        // **************************************************
        private void DisplayDataInUi(string videoFileName)
        {
            FishLens_App.Models.Video vid = GetData(videoFileName);

            videoName.Text = vid.Name;
            videoDateTime.Text = $"Duration: {vid.StartTime}s - {vid.EndTime}s";
            fishPresentStatus.Text = vid.LikelyClass == "fish" ? "Present" : "Not Present";
            fishPresentConfidence.Text = vid.AvgConfidence.ToString();
            travelDirection.Text = CapitalizeFirstLetter(vid.Direction);
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
        private void CreateVideoButtonsList(List<(FileInfo videoFile, FishLens_App.Models.Video videoData)> videoDataList)
        {
            CreateFolderHeader();
            CreateVideoButtons(videoDataList);
        }

        // **************************************************
        // Function: CreateFolderHeader
        // Description: Creates folder name display with checkbox and separator
        // **************************************************
        private void CreateFolderHeader()
        {
            Grid folderNameGrid = new Grid();
            folderNameGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            folderNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Folder name
            TextBox textBox = new TextBox();
            textBox.Text = _currentFolderName;
            textBox.Foreground = new SolidColorBrush(Colors.White);
            textBox.Background = Brushes.Transparent;
            textBox.BorderThickness = new Thickness(0);
            textBox.IsReadOnly = true;

            // Folder deletion checkbox - this one should select all the checkboxes in the folder
            CheckBox folderCheckBox = new CheckBox();
            folderCheckBox.Padding = new Thickness(5);
            folderCheckBox.VerticalAlignment = VerticalAlignment.Center;

            // capture folder name at time of header creation so handlers affect only this folder's videos
            string thisFolder = _currentFolderName;
            folderNameGrid.Tag = $"header:{thisFolder}";

            // When folder checkbox toggled, check/uncheck all video checkboxes belonging to this folder
            folderCheckBox.Checked += (s, e) =>
            {
                foreach (var child in videoList.Children)
                {
                    if (child is Grid g && g.Tag is string t && t == thisFolder)
                    {
                        foreach (var elem in g.Children)
                        {
                            if (elem is CheckBox cb)
                            {
                                cb.IsChecked = true;
                            }
                        }
                    }
                }
            };

            folderCheckBox.Unchecked += (s, e) =>
            {
                foreach (var child in videoList.Children)
                {
                    if (child is Grid g && g.Tag is string t && t == thisFolder)
                    {
                        foreach (var elem in g.Children)
                        {
                            if (elem is CheckBox cb)
                            {
                                cb.IsChecked = false;
                            }
                        }
                    }
                }
            };

            // Add elements
            Grid.SetColumn(folderCheckBox, 1);
            folderNameGrid.Children.Add(textBox);
            folderNameGrid.Children.Add(folderCheckBox);
            videoList.Children.Add(folderNameGrid);

            // Horizontal line separator
            Separator separator = new Separator();
            separator.Margin = new Thickness(0, 5, 0, 5);
            videoList.Children.Add(separator);
        }

        // **************************************************
        // Function: CreateVideoButtons
        // Description: Creates individual video buttons with checkboxes
        // **************************************************
        private void CreateVideoButtons(List<(FileInfo videoFile, FishLens_App.Models.Video videoData)> videoDataList)
        {
            foreach (var (videoFile, videoData) in videoDataList)
            {
                Grid grid = new Grid();
                // tag this grid with the current folder name so folder header can target it
                grid.Tag = _currentFolderName;
                grid.HorizontalAlignment = HorizontalAlignment.Stretch;
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Video button
                Button button = CreateSingleVideoButton(videoFile, videoData);
                button.Click += VideoButtonClick;
                Grid.SetColumn(button, 0);

                // Video deletion checkbox
                CheckBox checkBox = new CheckBox();
                checkBox.Padding = new Thickness(5);
                checkBox.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(checkBox, 1);

                grid.Children.Add(button);
                grid.Children.Add(checkBox);
                videoList.Children.Add(grid);
            }
        }

        // **************************************************
        // Function: CreateSingleVideoButton
        // Description: Creates styled button for a single video
        // Notes: Helper function for CreateVideoButtonsList
        // **************************************************
        private Button CreateSingleVideoButton(FileInfo videoFile, FishLens_App.Models.Video videoData)
        {
            bool isLowConfidence = IsLowConfidence(videoData.AvgConfidence);

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