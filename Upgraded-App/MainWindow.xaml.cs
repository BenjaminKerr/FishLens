// **************************************************
// ***********************************
// File: MainWindow.xaml.cs
// Description: Handles the analysis page's functionality
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using DocumentFormat.OpenXml.Spreadsheet;
using FishLens_App.Interfaces;
using FishLens_App.Models;
using FishLens_App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualBasic.FileIO;
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

        // UI constants
        private const int BUTTON_HEIGHT = 45;
        private const int BUTTON_FONT_SIZE = 13;
        private const int BUTTON_MARGIN = 5;
        private const int BUTTON_PADDING_HORIZONTAL = 12;
        private const int BUTTON_PADDING_VERTICAL = 8;
        private const int BUTTON_CORNER_RADIUS = 6;
        private const int CONTENT_PRESENTER_MARGIN = 8;
        private const int SIDEBAR_WIDTH = 320;

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
        private readonly UserSettings _config;
        private readonly IUiDialogService _uiDialogService;
        private readonly Stack<DeletionBatch> _deletionHistory = new Stack<DeletionBatch>();        // Stack of deletion batches so you can undo last deletion(s)

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
        public MainWindow(IProjectPathResolver pathResolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger, IUiDialogService uiDialogService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            _uiDialogService = uiDialogService ?? new UiDialogService();

            InitializeComponent();

            _config = GetConfigurationFromApplication();

            ThemeHelper.ThemeSwap(_config?.HighContrastMode ?? false);
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
        // Function: GetConfigurationFromApplication
        // Description: Retrieves AppConfiguration instance from application
        // **************************************************
        private UserSettings GetConfigurationFromApplication()
        {
            return App.Settings;
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
            _uiDialogService.ShowMessage($"Cannot create directory: {errorMessage}", "Directory Creation Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region Page Navigation

        // **************************************************
        // Function: HomeButtonClick
        // Description: Navigates to the home page
        // **************************************************
        private void HomeButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsCurrentPageSettings())
            {
                if (CheckForUnsavedChanges())
                {
                    ExpandSidebar();
                    MainFrame.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                ExpandSidebar();
                MainFrame.Visibility = Visibility.Collapsed;
            }
        }

        // **************************************************
        // Function: HistoryButtonClick
        // Description: Navigates to the history page
        // **************************************************
        private void HistoryButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsCurrentPageSettings())
            {
                if (CheckForUnsavedChanges())
                {
                    CollapseSidebar();
                    NavigateToPage(new History(_pathResolver, _fileSystemManager, _logger), "History");
                }
            }
            else
            {
                CollapseSidebar();
                NavigateToPage(new History(_pathResolver, _fileSystemManager, _logger), "History");
            }
        }

        // **************************************************
        // Function: SettingsButtonClick
        // Description: Navigates to the settings page
        // **************************************************
        private void SettingsButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsCurrentPageSettings())
            {
                if (CheckForUnsavedChanges())
                {
                    // Already on settings page, just need to refresh with current config
                    NavigateToPage(new Settings(_pathResolver, _fileSystemManager, _logger), "Settings");
                }
            }
            else
            {
                CollapseSidebar();
                NavigateToPage(new Settings(_pathResolver, _fileSystemManager, _logger), "Settings");
            }
        }

        public bool IsCurrentPageSettings()
        {
            return MainFrame.Content is Settings;
        }

        // **************************************************
        // Function: NavigateToPage
        // Description: Handles logic common to both navigation functions
        // **************************************************
        public void NavigateToPage(object page, string pageName)
        {
            MainFrame.Visibility = Visibility.Visible;
            _logger.LogInformation("Navigating to {PageName}", pageName);

            try
            {
                MainFrame.Navigate(page);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to navigate to {PageName}", pageName);
                _uiDialogService.ShowMessage($"Navigation Error: {ex.Message}", "Navigation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public bool CheckForUnsavedChanges()
        {
            var page = MainFrame.Content as Settings;
            if (page == null)
                return false;

            var (confidence, hideOutput, hideErrors, highContrast, largeText) = page.GetCurrentValues();

            bool hasUnsavedChanges =
                confidence != (_config.ConfidenceThreshold * 100.0) || //TODO: Threshold is divided by 100 in settings.xaml.cs to save in _config,
                                                                       //then mutlipled by 100 here to compare. This is a bit error-prone; consider
                                                                       //saving threshold as a 0-100 value in _config to avoid this.
                hideOutput != _config.OutputBox ||
                hideErrors != _config.ErrorBox ||
                highContrast != _config.HighContrastMode ||
                largeText != _config.LargeText;

            if (hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "Unsaved settings changes. Do you want to leave the page?",
                    "Settings",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                return result == MessageBoxResult.Yes;
            }

            return true;
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
            ProcessStartInfo processStartInfo = CreateYoloProcessStartInfo();

            // TODO: Avoid using multiple Parent.Parent chains to find project root.
            //       Use IProjectPathResolver or a robust helper instead.
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.Parent.FullName;
            string scriptDirectory = System.IO.Path.Combine(projectRoot, "main.py");

            string pythonExe = System.IO.Path.Combine( // Get the path to the python executable
                projectRoot,
                "Python",
                "venv",
                "Scripts",
                "python.exe" // resulting path should be something like C:\path\to\project\Python\venv\Scripts\python.exe
            );
            // TODO: Ask Aden whether pythonExe is necessary, since it isn't used in the ProcessStartInfo

            // TODO: Ensure casing ("scripts" vs "Scripts") is consistent across platforms (case-sensitive filesystems).
            processStartInfo.FileName = System.IO.Path.Combine(projectRoot, "venv", "scripts", "python.exe");
            string sampleDataPath = System.IO.Path.Combine(projectRoot, "sample_data");
            processStartInfo.Arguments = $"\"{scriptDirectory}\" \"{sampleDataPath}\"";
            processStartInfo.UseShellExecute = false;

            try
            {
                // TODO: Check RedirectStandardOutput/Error before calling ReadToEnd (CreateYoloProcessStartInfo sets these based on _checkBoxes).
                // TODO: Handle Process.Start returning null and add a timeout to WaitForExit to avoid hangs.
                Process process = Process.Start(processStartInfo);
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                _uiDialogService.ShowMessage($"Output:\n{output}\n\nErrors:\n{error}", "YOLO Output", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run YOLO process");
                _uiDialogService.ShowMessage(ex.Message, "Could not process videos.", MessageBoxButton.OK, MessageBoxImage.Error);
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

            // TODO: Ensure this method's choices (FileName = "python") are consistent with RunYolo which attempts to set an explicit python exe path.
            return new ProcessStartInfo
            {
                FileName = "python", // Lowercase 'p' for cross-platform compatibility
                Arguments = $"\"{yoloScriptDirectory}\" \"{sampleDataPath}\"",
                UseShellExecute = false
            };
        }

        // **************************************************
        // Function: ExecuteYoloProcess
        // Description: Runs YOLO process and handles output
        // TODO: This function isn't being used right now.
        //       We should consider its significance and reintegrate
        //       or delete it as needed.
        // **************************************************
        private async Task ExecuteYoloProcess(ProcessStartInfo processInfo, ProgressDialog progressDialog)
        {
            // TODO: Consider moving process execution and reading logic into a shared helper to avoid duplication with RunYolo.
            await Task.Run(() =>
            {
                using (Process process = Process.Start(processInfo))
                {
                    string output = App.Settings.OutputBox.ToString();
                    string error = App.Settings.ErrorBox.ToString();

                    process.WaitForExit();

                        Dispatcher.Invoke(() => progressDialog.Close());

                        DisplayProcessOutputIfNeeded(output, error);
                }
            });
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
                    _uiDialogService.ShowMessage($"Output:\n{output}\n\nErrors:\n{error}", "Process Output", MessageBoxButton.OK, MessageBoxImage.Information)
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
            EnterDataInFile(inputFolder, outputDirectory);
            await RunYolo();
            ShowVideosInUi(outputDirectory);
        }

        // **************************************************
        // Function: ShowVideosInUi
        // Description: Loads processed videos and their data into the UI
        // **************************************************
        private void ShowVideosInUi(string videoDirectory)
        {
            List<(FileInfo vid, FishLens_App.Models.Video data)> videoDataList = CreateSortedListOfVideos(videoDirectory);
            CreateVideoButtonsList(videoDataList);

            // If configured, load (and auto-play) the first uploaded video
            try
            {
                if (videoDataList != null && videoDataList.Count > 0)
                {
                    var firstVideoPath = videoDataList[0].vid.FullName;
                    DisplayDataInUi(firstVideoPath);
                    // Load the video into the player. LoadVideoInPlayer will respect _config.AutoPlayVideos
                    Dispatcher.Invoke(() => LoadVideoInPlayer(firstVideoPath));
                }
            }
            catch
            {
                _logger.LogWarning("Failed to auto-load first video after processing. This may be expected if no videos were processed or if there was an issue with the first video.");
            }
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
                if (!IsVideoFile(file))
                {
                    _logger.LogInformation("Skipping non-video file: {FileName}", file.FullName);
                    continue;
                }
                _fileSystemManager.CopyFile(file.FullName, Path.Combine(outputDirectory, file.Name));
            }
        }

        #endregion

        #region Video Data Management


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
        // Function: DeleteSelectedVideosClick
        // Description: Deletes selected videos and their analysis data
        // **************************************************
        public void DeleteSelectedVideosClick(object sender, EventArgs e)
        {
            var selected = GetSelectedVideoGrids();

            if (selected.Count != 0 && ConfirmDelete(selected.Count))
            {
                string csvPath = _pathResolver.ResolveCsvScriptPath();

                DeleteFilesAndCsvEntries(selected.Select(x => x.path).ToList(), csvPath);

                RemoveUiGrids(selected.Select(x => x.grid).ToList());
                _uiDialogService.ShowMessage($"Deleted {selected.Count} video(s).", "Delete Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
                if (child is Grid grid)
                {
                    CheckBox checkBox = null;
                    Button button = null;
                    foreach (var element in grid.Children)
                    {
                        if (element is CheckBox testingCheckBox)
                            checkBox = testingCheckBox;
                        if (element is Button testingButton)
                            button = testingButton;
                    }

                    if (checkBox != null && checkBox.IsChecked == true && button != null && button.Tag is string path)
                    {
                        result.Add((grid, path));
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
            return _uiDialogService.Confirm($"Delete {count} selected video(s) and remove their analysis?", "Confirm Delete");
        }

        // **************************************************
        // Function: DeleteFilesAndCsvEntries
        // Description: Deletes files and removes their CSV rows
        // **************************************************
        private void DeleteFilesAndCsvEntries(List<string> paths, string csvPath)
        {
            // Create a trash folder to hold deleted files for potential undo
            string trashRoot = Path.Combine(_pathResolver.ResolvePath(SAVED_VIDEOS_FOLDER), TRASH_FOLDER);
            Directory.CreateDirectory(trashRoot);
            var batch = new DeletionBatch { TrashFolder = Path.Combine(trashRoot, Guid.NewGuid().ToString()) };
            Directory.CreateDirectory(batch.TrashFolder);

            for (int i = 0; i < paths.Count; i++)
            {
                string fullPath = paths[i];
                string fileName = Path.GetFileName(fullPath);

                string csvRow = null;
                var videoData = new FishLens_App.Models.Video();

                // Capture video data and CSV row before removal (safe operations)
                try
                {
                    csvRow = FindCsvRowForFile(csvPath, fileName);
                    videoData = FishLens_App.Services.CsvUtils.ReadVideoFromCsv(csvPath, fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read CSV data for file {path} during deletion. This may affect undo functionality for this file.", fullPath);
                }

                // Stop video player if needed (safe operation)
                StopVideoPlayerIfPlayingFile(fullPath);

                // Prepare paths
                string trashPath = Path.Combine(batch.TrashFolder, fileName);
                string folderTag = GetFolderTagForPath(fullPath);

                try
                {
                    // Only the actual file system operations in the try block
                    if (File.Exists(fullPath))
                    {
                        File.Move(fullPath, trashPath, overwrite: true);
                    }

                    batch.Items.Add((fullPath, trashPath, csvRow, videoData, folderTag));

                    if (csvRow != null)
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
        // Function: FindCsvRowForFile
        // Description: Finds the CSV row corresponding to a given video file
        // **************************************************
        private string FindCsvRowForFile(string csvPath, string fileName)
        {
            if (!File.Exists(csvPath))
                return null;

            using (TextFieldParser parser = new TextFieldParser(csvPath))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(",");
                // Skip header
                if (!parser.EndOfData)
                    parser.ReadLine();
                while (!parser.EndOfData)
                {
                    string[] fields = parser.ReadFields();
                    if (fields.Length > 0 && string.Equals(fields[0].Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Join(",", fields);
                    }
                }
            }
            return null;
        }

        // **************************************************
        // Function: StopVideoPlayerIfPlayingFile
        // Description: Stops the video player if it's currently playing the specified file
        // **************************************************
        private void StopVideoPlayerIfPlayingFile(string fullPath)
        {
            if (videoPlayer.Source != null &&
                string.Equals(videoPlayer.Source.LocalPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                videoPlayer.Stop();
                videoPlayer.Source = null;
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
            catch
            {
                _logger.LogWarning("Failed to get folder tag for path {path}", path);
            }
            return null;
        }

        // **************************************************
        // Function: UndoLastDeleteClick
        // Description: UI handler to undo the most recent deletion batch
        // **************************************************
        public void UndoLastDeleteClick(object sender, EventArgs e)
        {
            if (_deletionHistory.Count == 0)
            {
                _uiDialogService.ShowMessage("Nothing to undo.", "Undo", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    _uiDialogService.ShowMessage($"Failed to restore file: {item.originalPath}\nError: {ex.Message}", "Restore Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                _uiDialogService.ShowMessage("Failed to restore CSV data for some videos. Their analysis data may be missing from the UI.", "CSV Restore Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // restore UI
            RestoreUiForFiles(restoredFiles, batch);

            // cleanup trash folder
            try 
            { 
                if (Directory.Exists(batch.TrashFolder)) 
                    Directory.Delete(batch.TrashFolder, recursive: true); 
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Trash folder could not be deleted.");
            }

            _uiDialogService.ShowMessage($"Restored {restoredFiles.Count} file(s).", "Undo Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: RestoreUiForFiles
        // Description: Recreates UI entries for restored files using saved video data
        // Refactor: split into small helpers for clarity and testability
        // **************************************************
        private void RestoreUiForFiles(List<string> restoredFiles, DeletionBatch batch)
        {
            foreach (var filePath in restoredFiles)
            {
                // find corresponding item in batch
                var item = batch.Items.Find(x => string.Equals(x.originalPath, filePath, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(item.originalPath))
                    continue;

                EnsureFolderHeaderExists(item.folder);

                Grid grid = CreateGridForRestoredFile(item.originalPath, item.video, item.folder);
                videoList.Children.Add(grid);
            }
        }

        // **************************************************
        // Function: EnsureFolderHeaderExists
        // Description: Adds a folder header for the given folder if one is not already present
        // **************************************************
        private void EnsureFolderHeaderExists(string folder)
        {
            foreach (var child in videoList.Children)
            {
                if (child is Grid g && g.Tag is string t && t == $"header:{folder}")
                    return; // header already present
            }

            // temporarily set _currentFolderName so CreateFolderHeader produces the correct header
            var previousFolder = _currentFolderName;
            _currentFolderName = folder;
            CreateFolderHeader();
            _currentFolderName = previousFolder;
        }

        // **************************************************
        // Function: CreateGridForRestoredFile
        // Description: Builds the Grid containing the restored video's Button and CheckBox
        // **************************************************
        private Grid CreateGridForRestoredFile(string filePath, FishLens_App.Models.Video videoData, string folder)
        {
            var grid = new Grid
            {
                Tag = folder,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FileInfo fi = new FileInfo(filePath);
            Button button = CreateSingleVideoButton(fi, videoData);
            button.Click += VideoButtonClick;
            Grid.SetColumn(button, 0);

            var checkBox = new CheckBox
            {
                Padding = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(checkBox, 1);

            grid.Children.Add(button);
            grid.Children.Add(checkBox);

            return grid;
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
                _uiDialogService.ShowMessage("Analysis data file not found.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return vid;
            }

            try
            {
                // TODO: Ensure ReadVideoFromCsv is tolerant to unexpected input; consider returning null and handling upstream.
                return FishLens_App.Services.CsvUtils.ReadVideoFromCsv(csvPath, videoFileName);
            }
            catch (Exception ex)
            {
                _uiDialogService.ShowMessage($"Error reading analysis data: {ex.Message}", "Error",
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
                    _uiDialogService.ShowMessage("No analysis data found to export.", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string defaultName = $"FishLens_Analysis_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
                string selectedPath = _uiDialogService.ShowSaveFileDialog("Excel Files (*.xlsx)|*.xlsx", ".xlsx", defaultName);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    MakeExcelSheetAndInsertData(selectedPath, csvPath);
                }
            }
            catch (Exception ex)
            {
                _uiDialogService.ShowMessage($"Error exporting data: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // **************************************************
        // Function: CreateExportSaveDialog
        // Description: Creates configured SaveFileDialog for Excel export
        // **************************************************
        // Export dialog is provided by IUiDialogService.ShowSaveFileDialog; keep helper removed.

        // **************************************************
        // Function: MakeExcelSheetAndInsertData
        // Description: Creates Excel workbook and populates with CSV data
        // Notes: Helper function for ExportDataClick
        // **************************************************
        private void MakeExcelSheetAndInsertData(string excelPath, string csvPath)
        {
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
            _uiDialogService.ShowMessage($"Data exported successfully to:\n{excelPath}", "Export Successful",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: PromptToOpenExportedFile
        // Description: Asks user if they want to open the exported file
        // **************************************************
        private void PromptToOpenExportedFile(string excelPath)
        {
            if (_uiDialogService.Confirm("Would you like to open the exported file?", "Open File"))
            {
                _uiDialogService.OpenFile(excelPath);
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
                    _uiDialogService.ShowMessage("No video selected to save changes for.", "Save Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string csvPath = _pathResolver.ResolveCsvScriptPath();

                if (!File.Exists(csvPath))
                {
                    _uiDialogService.ShowMessage("CSV file not found.", "Save Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                UpdateCsvFile(csvPath, currentVideoName);

                _uiDialogService.ShowMessage("Changes saved successfully!", "Save Successful",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving changes to CSV");
                _uiDialogService.ShowMessage($"Error saving changes: {ex.Message}", "Save Error",
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
                To = (SIDEBAR_WIDTH / 3),
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new System.Windows.Media.Animation.CubicEase()
            };

            SideBar.BeginAnimation(System.Windows.Controls.Border.WidthProperty, animation);

            videoList.Visibility = Visibility.Collapsed;
            deleteSelectedVideos.Visibility = Visibility.Collapsed;
            undoLastDelete.Visibility = Visibility.Collapsed;
            sidebarSeperator.Visibility = Visibility.Collapsed;
            videoLibraryTitle.Visibility = Visibility.Collapsed;

            ButtonGrid.RowDefinitions.Clear();
            ButtonGrid.ColumnDefinitions.Clear();

            // Reformat nav buttons to vertical layout
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
                To = SIDEBAR_WIDTH,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new System.Windows.Media.Animation.CubicEase()
            };

            SideBar.BeginAnimation(System.Windows.Controls.Border.WidthProperty, animation);

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
            DisplayDataInUi(videoPath);

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
            textBox.Foreground = new SolidColorBrush(System.Windows.Media.Colors.White);
            textBox.Background = Brushes.Transparent;
            textBox.BorderThickness = new Thickness(0);
            textBox.IsReadOnly = true;

            // Folder deletion checkbox - this one should select all the checkboxes in the folder
            CheckBox folderCheckBox = new CheckBox();
            folderCheckBox.Padding = new Thickness(5);
            folderCheckBox.VerticalAlignment = VerticalAlignment.Center;

            // Capture folder name at time of header creation so handlers affect only this folder's videos
            string thisFolder = _currentFolderName;
            folderNameGrid.Tag = $"header:{thisFolder}";

            // When folder checkbox toggled, check/uncheck all video checkboxes belonging to this folder
            folderCheckBox.Checked += (s, e) =>
            {
                foreach (var child in videoList.Children)
                {
                    ToggleFolderCheckboxes(thisFolder, true);
                }
            };

            folderCheckBox.Unchecked += (s, e) =>
            {
                ToggleFolderCheckboxes(thisFolder, false);
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
        // Function: ToggleFolderCheckboxes
        // Description: Sets all video checkboxes for a folder to checked or unchecked
        // **************************************************
        private void ToggleFolderCheckboxes(string folderName, bool isChecked)
        {
            foreach (var child in videoList.Children)
            {
                if (child is Grid g && g.Tag is string t && t == folderName)
                {
                    foreach (var elem in g.Children)
                    {
                        if (elem is CheckBox cb)
                        {
                            cb.IsChecked = isChecked;
                        }
                    }
                }
            }
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

            var button = new Button
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
                Cursor = Cursors.Hand
            };

            // Prefer resource-defined styles; fall back to programmatic style for safety.
            try
            {
                var resourceKey = isLowConfidence ? "VideoButtonLowConfidenceStyle" : "VideoButtonNormalStyle";
                var style = TryFindResource(resourceKey) as Style;
                if (style != null)
                    button.Style = style;
                else
                    button.Style = CreateButtonStyle(isLowConfidence);
            }
            catch
            {
                button.Style = CreateButtonStyle(isLowConfidence);
            }

            return button;
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
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 242, 242))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 250, 251))));

            style.Setters.Add(new Setter(Button.ForegroundProperty,
                isLowConfidence
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81))));

            style.Setters.Add(new Setter(Button.BorderBrushProperty,
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235))));
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
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.Name = "border";
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(BUTTON_CORNER_RADIUS));

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
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246)), "border"));

            trigger.Setters.Add(new Setter(Button.ForegroundProperty,
                isLowConfidence
                    ? new SolidColorBrush(System.Windows.Media.Colors.White)
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39))));

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
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235)), "border"));

            return trigger;
        }

        #endregion
    }
}