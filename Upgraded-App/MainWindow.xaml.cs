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
        // **************************************************
        public MainWindow() : this(
            GetDefaultProjectPathResolver(),
            GetDefaultFileSystemManager(),
            NullLogger<MainWindow>.Instance)
        {
        }

        #endregion

        #region Dependency Creation Helpers

        private static IProjectPathResolver GetDefaultProjectPathResolver() => new DefaultProjectPathResolver();
        private static IFileSystemManager GetDefaultFileSystemManager() => new StandardFileSystemManager();
        private UserSettings GetConfigurationFromApplication() => App.Settings;

        #endregion

        #region Resource Helpers

        // **************************************************
        // Function: ResBrush
        // Description: Resolves a SolidColorBrush from the app ResourceDictionary,
        //              falling back to a hardcoded hex if the key is missing.
        //              Keeps code-behind in sync with XAML resource changes.
        // **************************************************
        private SolidColorBrush ResBrush(string key, string fallbackHex)
        {
            if (TryFindResource(key) is SolidColorBrush brush)
                return brush;
            return new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(fallbackHex));
        }

        #endregion

        #region Directory Management

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

        private void HandleDirectoryCreationError(string errorMessage)
        {
            _uiDialogService.ShowMessage($"Cannot create directory: {errorMessage}", "Directory Creation Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region Page Navigation

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

        private void SettingsButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsCurrentPageSettings())
            {
                if (CheckForUnsavedChanges())
                    NavigateToPage(new Settings(_pathResolver, _fileSystemManager, _logger), "Settings");
            }
            else
            {
                CollapseSidebar();
                NavigateToPage(new Settings(_pathResolver, _fileSystemManager, _logger), "Settings");
            }
        }

        public bool IsCurrentPageSettings() => MainFrame.Content is Settings;

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

            // Always read from App.Settings directly so we compare against the
            // last *saved* state, not the stale snapshot captured at construction.
            var saved = App.Settings;

            bool hasUnsavedChanges =
                confidence != (saved.ConfidenceThreshold * 100.0) ||
                hideOutput != saved.OutputBox ||
                hideErrors != saved.ErrorBox ||
                highContrast != saved.HighContrastMode ||
                largeText != saved.LargeText;

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

        private async Task RunYolo()
        {
            ProcessStartInfo processStartInfo = CreateYoloProcessStartInfo();

            // TODO: Avoid using multiple Parent.Parent chains to find project root.
            //       Use IProjectPathResolver or a robust helper instead.
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.Parent.FullName;
            string scriptDirectory = System.IO.Path.Combine(projectRoot, "main.py");

            string pythonExe = System.IO.Path.Combine(
                projectRoot, "Python", "venv", "Scripts", "python.exe"
            );
            // TODO: Ask Aden whether pythonExe is necessary, since it isn't used in the ProcessStartInfo

            // TODO: Ensure casing ("scripts" vs "Scripts") is consistent across platforms.
            processStartInfo.FileName = System.IO.Path.Combine(projectRoot, "venv", "scripts", "python.exe");
            string sampleDataPath = System.IO.Path.Combine(projectRoot, "sample_data");
            processStartInfo.Arguments = $"\"{scriptDirectory}\" \"{sampleDataPath}\"";
            processStartInfo.UseShellExecute = false;

            try
            {
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

        private ProcessStartInfo CreateYoloProcessStartInfo()
        {
            string yoloScriptDirectory = _pathResolver.ResolveYoloScriptPath();
            string sampleDataPath = Path.Combine(_pathResolver.ResolveProjectRoot(), SAMPLE_DATA_FOLDER);

            return new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{yoloScriptDirectory}\" \"{sampleDataPath}\"",
                UseShellExecute = false
            };
        }

        // TODO: ExecuteYoloProcess is currently unused — evaluate reintegration or removal.
        private async Task ExecuteYoloProcess(ProcessStartInfo processInfo, ProgressDialog progressDialog)
        {
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

        private async void ProcessVideos(string inputFolder, string outputDirectory)
        {
            MakeDirectoryIfNotExists(outputDirectory);
            EnterDataInFile(inputFolder, outputDirectory);
            await RunYolo();
            ShowVideosInUi(outputDirectory);
        }

        private void ShowVideosInUi(string videoDirectory)
        {
            List<(FileInfo vid, FishLens_App.Models.Video data)> videoDataList = CreateSortedListOfVideos(videoDirectory);
            CreateVideoButtonsList(videoDataList);

            try
            {
                if (videoDataList != null && videoDataList.Count > 0)
                {
                    var firstVideoPath = videoDataList[0].vid.FullName;
                    DisplayDataInUi(firstVideoPath);
                    Dispatcher.Invoke(() => LoadVideoInPlayer(firstVideoPath));
                }
            }
            catch
            {
                _logger.LogWarning("Failed to auto-load first video after processing.");
            }
        }

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
        // Description: Updates UI elements with video analysis data.
        //
        // FIX: ComboBox.Text is not a settable property that selects items —
        //      it only works for editable ComboBoxes. We now walk the Items
        //      collection and select the matching ComboBoxItem by content,
        //      falling back to index 0 if no match is found.
        // **************************************************
        private void DisplayDataInUi(string videoFileName)
        {
            FishLens_App.Models.Video vid = GetData(videoFileName);

            videoName.Text = vid.Name;
            videoDateTime.Text = $"Duration: {vid.StartTime}s - {vid.EndTime}s";

            // Fish present status
            string statusText = vid.LikelyClass == "fish" ? "Present" : "Not Present";
            SelectComboBoxItemByContent(fishPresentStatus, statusText);

            fishPresentConfidence.Text = vid.AvgConfidence.ToString();

            // Travel direction — capitalize for display matching
            string directionText = CapitalizeFirstLetter(vid.Direction);
            SelectComboBoxItemByContent(travelDirection, directionText);
        }

        // **************************************************
        // Function: SelectComboBoxItemByContent
        // Description: Finds and selects the ComboBoxItem whose Content matches
        //              the target string (case-insensitive). Falls back to
        //              SelectedIndex = 0 if no match is found.
        // **************************************************
        private void SelectComboBoxItemByContent(ComboBox comboBox, string targetContent)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem cbi &&
                    string.Equals(cbi.Content?.ToString(), targetContent, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = cbi;
                    return;
                }
            }

            // No match — default to first item rather than leaving stale selection
            if (comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

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
                        if (element is CheckBox testingCheckBox) checkBox = testingCheckBox;
                        if (element is Button testingButton) button = testingButton;
                    }

                    if (checkBox != null && checkBox.IsChecked == true && button != null && button.Tag is string path)
                        result.Add((grid, path));
                }
            }
            return result;
        }

        private bool ConfirmDelete(int count) =>
            _uiDialogService.Confirm($"Delete {count} selected video(s) and remove their analysis?", "Confirm Delete");

        private void DeleteFilesAndCsvEntries(List<string> paths, string csvPath)
        {
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

                try
                {
                    csvRow = FindCsvRowForFile(csvPath, fileName);
                    videoData = FishLens_App.Services.CsvUtils.ReadVideoFromCsv(csvPath, fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read CSV data for file {path} during deletion.", fullPath);
                }

                StopVideoPlayerIfPlayingFile(fullPath);

                string trashPath = Path.Combine(batch.TrashFolder, fileName);
                string folderTag = GetFolderTagForPath(fullPath);

                try
                {
                    if (File.Exists(fullPath))
                        File.Move(fullPath, trashPath, overwrite: true);

                    batch.Items.Add((fullPath, trashPath, csvRow, videoData, folderTag));

                    if (csvRow != null)
                        FishLens_App.Services.CsvUtils.RemoveVideoFromCsv(csvPath, fileName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete/move file {path}", fullPath);
                }
            }

            if (batch.Items.Count > 0)
                _deletionHistory.Push(batch);
        }

        private string FindCsvRowForFile(string csvPath, string fileName)
        {
            if (!File.Exists(csvPath)) return null;

            using (TextFieldParser parser = new TextFieldParser(csvPath))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(",");
                if (!parser.EndOfData) parser.ReadLine(); // skip header
                while (!parser.EndOfData)
                {
                    string[] fields = parser.ReadFields();
                    if (fields.Length > 0 && string.Equals(fields[0].Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                        return string.Join(",", fields);
                }
            }
            return null;
        }

        private void StopVideoPlayerIfPlayingFile(string fullPath)
        {
            if (videoPlayer.Source != null &&
                string.Equals(videoPlayer.Source.LocalPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                videoPlayer.Stop();
                videoPlayer.Source = null;
            }
        }

        private void RemoveUiGrids(List<Grid> grids)
        {
            foreach (var g in grids)
                videoList.Children.Remove(g);

            var headersToRemove = new List<UIElement>();
            foreach (var child in videoList.Children)
            {
                if (child is Grid headerGrid && headerGrid.Tag is string t && t.StartsWith("header:"))
                {
                    string folder = t.Substring("header:".Length);
                    bool anyRemaining = videoList.Children
                        .OfType<Grid>()
                        .Any(g2 => g2.Tag is string t2 && t2 == folder);

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
                        videoList.Children.RemoveAt(idx);
                }
            }
        }

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

        public void UndoLastDeleteClick(object sender, EventArgs e)
        {
            if (_deletionHistory.Count == 0)
            {
                _uiDialogService.ShowMessage("Nothing to undo.", "Undo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var batch = _deletionHistory.Pop();
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

            try
            {
                if (rowsToRestore.Count > 0)
                    FishLens_App.Services.CsvUtils.AppendRows(_pathResolver.ResolveCsvScriptPath(), rowsToRestore);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore CSV rows");
                _uiDialogService.ShowMessage("Failed to restore CSV data for some videos.", "CSV Restore Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            RestoreUiForFiles(restoredFiles, batch);

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

        private void RestoreUiForFiles(List<string> restoredFiles, DeletionBatch batch)
        {
            foreach (var filePath in restoredFiles)
            {
                var item = batch.Items.Find(x => string.Equals(x.originalPath, filePath, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(item.originalPath)) continue;

                EnsureFolderHeaderExists(item.folder);
                Grid grid = CreateGridForRestoredFile(item.originalPath, item.video, item.folder);
                videoList.Children.Add(grid);
            }
        }

        private void EnsureFolderHeaderExists(string folder)
        {
            foreach (var child in videoList.Children)
            {
                if (child is Grid g && g.Tag is string t && t == $"header:{folder}")
                    return;
            }

            var previousFolder = _currentFolderName;
            _currentFolderName = folder;
            CreateFolderHeader();
            _currentFolderName = previousFolder;
        }

        private Grid CreateGridForRestoredFile(string filePath, FishLens_App.Models.Video videoData, string folder)
        {
            var grid = new Grid { Tag = folder, HorizontalAlignment = HorizontalAlignment.Stretch };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FileInfo fi = new FileInfo(filePath);
            Button button = CreateSingleVideoButton(fi, videoData);
            button.Click += VideoButtonClick;
            Grid.SetColumn(button, 0);

            var checkBox = new CheckBox { Padding = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(checkBox, 1);

            grid.Children.Add(button);
            grid.Children.Add(checkBox);
            return grid;
        }

        private List<(FileInfo vid, FishLens_App.Models.Video data)> CreateSortedListOfVideos(string directory)
        {
            DirectoryInfo vidsInfo = new DirectoryInfo(directory);
            FileInfo[] fileInfos = vidsInfo.GetFiles("*");

            var videoDataList = new List<(FileInfo vid, FishLens_App.Models.Video data)>();
            foreach (FileInfo vid in fileInfos)
            {
                if (IsVideoFile(vid))
                    videoDataList.Add((vid, GetData(vid.Name)));
            }

            return videoDataList.OrderBy(x => x.data.AvgConfidence).ToList();
        }

        private bool IsVideoFile(FileInfo file)
        {
            string ext = file.Extension.ToLower();
            return ext == ".mp4" || ext == ".asf";
        }

        private FishLens_App.Models.Video GetData(string videoFileName)
        {
            FishLens_App.Models.Video vid = new FishLens_App.Models.Video();
            string csvPath = _pathResolver.ResolveCsvScriptPath();

            if (!File.Exists(csvPath))
            {
                _uiDialogService.ShowMessage("Analysis data file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return vid;
            }

            try
            {
                return FishLens_App.Services.CsvUtils.ReadVideoFromCsv(csvPath, videoFileName);
            }
            catch (Exception ex)
            {
                _uiDialogService.ShowMessage($"Error reading analysis data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return vid;
            }
        }

        #endregion

        #region Data Export

        private void ExportDataClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string csvPath = _pathResolver.ResolveCsvScriptPath();

                if (!File.Exists(csvPath))
                {
                    _uiDialogService.ShowMessage("No analysis data found to export.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string defaultName = $"FishLens_Analysis_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
                string selectedPath = _uiDialogService.ShowSaveFileDialog("Excel Files (*.xlsx)|*.xlsx", ".xlsx", defaultName);
                if (!string.IsNullOrEmpty(selectedPath))
                    MakeExcelSheetAndInsertData(selectedPath, csvPath);
            }
            catch (Exception ex)
            {
                _uiDialogService.ShowMessage($"Error exporting data: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

        private void WriteDataToWorksheet(ClosedXML.Excel.IXLWorksheet worksheet, string[] allLines)
        {
            for (int line = 0; line < allLines.Length; line++)
            {
                string[] columns = allLines[line].Split(',');
                for (int column = 0; column < columns.Length; column++)
                    worksheet.Cell(line + 1, column + 1).Value = columns[column].Trim();
            }
        }

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

        private void ShowExportSuccessMessage(string excelPath) =>
            _uiDialogService.ShowMessage($"Data exported successfully to:\n{excelPath}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);

        private void PromptToOpenExportedFile(string excelPath)
        {
            if (_uiDialogService.Confirm("Would you like to open the exported file?", "Open File"))
                _uiDialogService.OpenFile(excelPath);
        }

        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentVideoName = videoName.Text;

                if (string.IsNullOrEmpty(currentVideoName) || currentVideoName == "--")
                {
                    _uiDialogService.ShowMessage("No video selected to save changes for.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string csvPath = _pathResolver.ResolveCsvScriptPath();

                if (!File.Exists(csvPath))
                {
                    _uiDialogService.ShowMessage("CSV file not found.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                UpdateCsvFile(csvPath, currentVideoName);
                _uiDialogService.ShowMessage("Changes saved successfully!", "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving changes to CSV");
                _uiDialogService.ShowMessage($"Error saving changes: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateCsvFile(string csvPath, string videoFileName)
        {
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

        private string CreateUpdatedCsvRow(string[] originalColumns)
        {
            string likelyClass = GetFishPresentClass();
            string direction = GetTravelDirectionValue();
            string species = fishSpecies.Text.Trim();

            string videoFile = originalColumns[0].Trim();
            string trackId = originalColumns[1].Trim();
            string confidence = originalColumns[3].Trim();
            string startTime = originalColumns[4].Trim();
            string endTime = originalColumns[5].Trim();
            string avgConfidence = originalColumns[6].Trim();

            if (!string.IsNullOrEmpty(species) && species != "--")
                likelyClass = species.ToLower();

            return $"{videoFile},{trackId},{likelyClass},{confidence},{startTime},{endTime},{avgConfidence},{direction}";
        }

        private string GetFishPresentClass()
        {
            var selectedItem = fishPresentStatus.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return "unknown";
            return selectedItem.Content.ToString() == "Present" ? "fish" : "not_fish";
        }

        private string GetTravelDirectionValue()
        {
            var selectedItem = travelDirection.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return "unknown";
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
            ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(Home, 0); Grid.SetColumn(Home, 0);
            Grid.SetRow(History, 1); Grid.SetColumn(History, 0);
            Grid.SetRow(Settings, 2); Grid.SetColumn(Settings, 0);
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

            videoList.Visibility = Visibility.Visible;
            deleteSelectedVideos.Visibility = Visibility.Visible;
            undoLastDelete.Visibility = Visibility.Visible;
            sidebarSeperator.Visibility = Visibility.Visible;
            videoLibraryTitle.Visibility = Visibility.Visible;

            ButtonGrid.RowDefinitions.Clear();
            ButtonGrid.ColumnDefinitions.Clear();
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(Home, 0); Grid.SetColumn(Home, 0);
            Grid.SetRow(History, 0); Grid.SetColumn(History, 1);
            Grid.SetRow(Settings, 0); Grid.SetColumn(Settings, 2);
        }

        private void VideoButtonClick(object sender, RoutedEventArgs e)
        {
            Button clickedButton = (Button)sender;
            string videoPath = clickedButton.Tag.ToString();

            LoadVideoInPlayer(videoPath);
            DisplayDataInUi(videoPath);

            string videoFileName = Path.GetFileName(videoPath);
            GetData(videoFileName);
        }

        private void LoadVideoInPlayer(string videoPath)
        {
            videoPlayer.Source = new Uri(videoPath);
            videoPlayer.Play();
        }

        private string CapitalizeFirstLetter(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        private void CreateVideoButtonsList(List<(FileInfo videoFile, FishLens_App.Models.Video videoData)> videoDataList)
        {
            CreateFolderHeader();
            CreateVideoButtons(videoDataList);
        }

        // **************************************************
        // Function: CreateFolderHeader
        // Description: Creates folder name display with checkbox and separator.
        //
        // FIX: Foreground was hardcoded to Colors.White. Now resolved from
        //      OnAccentForeground resource so it adapts to both themes.
        //      The folder header sits on the AccentBrush sidebar, so
        //      OnAccentForeground (always a light tone) is the right key.
        // **************************************************
        private void CreateFolderHeader()
        {
            var folderNameGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            folderNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBox = new TextBox
            {
                Text = _currentFolderName,
                Foreground = ResBrush("OnAccentForeground", "#F5F8FA"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };

            var folderCheckBox = new CheckBox
            {
                Padding = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center,
                // Checkbox tick/border on the teal sidebar reads better with foreground set explicitly
                Foreground = ResBrush("OnAccentForeground", "#F5F8FA")
            };

            string thisFolder = _currentFolderName;
            folderNameGrid.Tag = $"header:{thisFolder}";

            folderCheckBox.Checked += (s, e) => ToggleFolderCheckboxes(thisFolder, true);
            folderCheckBox.Unchecked += (s, e) => ToggleFolderCheckboxes(thisFolder, false);

            Grid.SetColumn(folderCheckBox, 1);
            folderNameGrid.Children.Add(textBox);
            folderNameGrid.Children.Add(folderCheckBox);
            videoList.Children.Add(folderNameGrid);

            var separator = new Separator
            {
                Margin = new Thickness(0, 5, 0, 5),
                Background = ResBrush("HoverBackgroundBrush", "#2D7A8F"),
                Opacity = 0.5
            };
            videoList.Children.Add(separator);
        }

        private void ToggleFolderCheckboxes(string folderName, bool isChecked)
        {
            foreach (var child in videoList.Children)
            {
                if (child is Grid g && g.Tag is string t && t == folderName)
                {
                    foreach (var elem in g.Children)
                    {
                        if (elem is CheckBox cb)
                            cb.IsChecked = isChecked;
                    }
                }
            }
        }

        private void CreateVideoButtons(List<(FileInfo videoFile, FishLens_App.Models.Video videoData)> videoDataList)
        {
            foreach (var (videoFile, videoData) in videoDataList)
            {
                var grid = new Grid
                {
                    Tag = _currentFolderName,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Button button = CreateSingleVideoButton(videoFile, videoData);
                button.Click += VideoButtonClick;
                Grid.SetColumn(button, 0);

                var checkBox = new CheckBox
                {
                    Padding = new Thickness(5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = ResBrush("OnAccentForeground", "#F5F8FA")
                };
                Grid.SetColumn(checkBox, 1);

                grid.Children.Add(button);
                grid.Children.Add(checkBox);
                videoList.Children.Add(grid);
            }
        }

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
                button.Style = style ?? CreateButtonStyle(isLowConfidence);
            }
            catch
            {
                button.Style = CreateButtonStyle(isLowConfidence);
            }

            return button;
        }

        private bool IsLowConfidence(double confidence) =>
            confidence < (_config?.ConfidenceThreshold ?? DEFAULT_CONFIDENCE_THRESHOLD);

        #endregion

        #region Button Styling

        // **************************************************
        // Function: CreateButtonStyle
        // Description: Creates video list button styles that adapt to both themes.
        //
        // FIX: Colors were hardcoded to light-theme values. In dark theme the
        //      normal button (#F9FAFB bg, #374151 fg) is nearly white on a dark
        //      card — unreadable. We now resolve CardBackground and PrimaryText
        //      from resources so the button adapts to the active theme.
        //
        //      Low-confidence buttons keep their red semantic coloring since
        //      red-on-any-background communicates "warning" regardless of theme.
        //      The hover state switches to white text so it stays readable on
        //      the red hover background in both themes.
        // **************************************************
        private Style CreateButtonStyle(bool isLowConfidence)
        {
            var style = new Style(typeof(Button));
            SetButtonDefaultAppearance(style, isLowConfidence);
            style.Setters.Add(new Setter(Button.TemplateProperty, CreateButtonControlTemplate(isLowConfidence)));
            return style;
        }

        private void SetButtonDefaultAppearance(Style style, bool isLowConfidence)
        {
            if (isLowConfidence)
            {
                // Low-confidence: soft red tint background, dark red text — semantic warning,
                // works on both light and dark card backgrounds.
                style.Setters.Add(new Setter(Button.BackgroundProperty,
                    new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 220, 38, 38)))); // semi-transparent red overlay
                style.Setters.Add(new Setter(Button.ForegroundProperty,
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68))));      // bright red — readable on dark & light
                style.Setters.Add(new Setter(Button.BorderBrushProperty,
                    new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 220, 38, 38)))); // subtle red border
            }
            else
            {
                // Normal: resolve from resources so the button matches the card in whichever theme is active.
                style.Setters.Add(new Setter(Button.BackgroundProperty,
                    ResBrush("CardBackground", "#FFFFFF")));
                style.Setters.Add(new Setter(Button.ForegroundProperty,
                    ResBrush("PrimaryText", "#0D3640")));
                style.Setters.Add(new Setter(Button.BorderBrushProperty,
                    ResBrush("BorderBrush", "#E1E8ED")));
            }
        }

        private ControlTemplate CreateButtonControlTemplate(bool isLowConfidence)
        {
            var template = new ControlTemplate(typeof(Button));
            template.VisualTree = CreateButtonBorder();
            AddButtonTriggers(template, isLowConfidence);
            return template;
        }

        private FrameworkElementFactory CreateButtonBorder()
        {
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.Name = "border";
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(BUTTON_CORNER_RADIUS));
            border.AppendChild(CreateContentPresenter());
            return border;
        }

        private FrameworkElementFactory CreateContentPresenter()
        {
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(CONTENT_PRESENTER_MARGIN, 0, CONTENT_PRESENTER_MARGIN, 0));
            return cp;
        }

        private void AddButtonTriggers(ControlTemplate template, bool isLowConfidence)
        {
            template.Triggers.Add(CreateHoverTrigger(isLowConfidence));
            template.Triggers.Add(CreatePressedTrigger(isLowConfidence));
        }

        private Trigger CreateHoverTrigger(bool isLowConfidence)
        {
            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };

            if (isLowConfidence)
            {
                // Solid red fill on hover — the warning becomes explicit
                trigger.Setters.Add(new Setter(Button.BackgroundProperty,
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)), "border"));
                trigger.Setters.Add(new Setter(Button.ForegroundProperty,
                    new SolidColorBrush(System.Windows.Media.Colors.White)));
            }
            else
            {
                // Subtle hover: slightly different shade of the card background.
                // BorderColorBrush in light = #E1E8ED (light gray), in dark = #444950 (dark gray).
                // Both look like a gentle highlight on their respective card backgrounds.
                trigger.Setters.Add(new Setter(Button.BackgroundProperty,
                    ResBrush("BorderColorBrush", "#E1E8ED"), "border"));
                trigger.Setters.Add(new Setter(Button.ForegroundProperty,
                    ResBrush("PrimaryText", "#0D3640")));
            }

            return trigger;
        }

        private Trigger CreatePressedTrigger(bool isLowConfidence)
        {
            var trigger = new Trigger { Property = Button.IsPressedProperty, Value = true };

            trigger.Setters.Add(new Setter(Button.BackgroundProperty,
                isLowConfidence
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38))
                    : ResBrush("AccentBrush", "#1B4F5C"),
                "border"));

            // On press, always flip to on-accent text so it reads on the teal press color
            if (!isLowConfidence)
            {
                trigger.Setters.Add(new Setter(Button.ForegroundProperty,
                    ResBrush("OnAccentForeground", "#F5F8FA")));
            }

            return trigger;
        }

        #endregion
    }
}