// **************************************************
// ***********************************
// File: MainWindow.xaml.cs
// Description: Handles the analysis page's functionality
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using FishLens_App.Interfaces;
using FishLens_App.Services;

namespace FishLens_App
{
    public partial class MainWindow : Window
    {
        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        AppConfiguration config = new AppConfiguration();

        //**************************************************
        // Function: Constructor
        // Description: Parameterized
        public MainWindow(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            InitializeComponent();
        }

        // **************************************************
        // Function: Constructor
        // Description: Unparameterized
        public MainWindow() : this(
            GetDefaultProjectPathResolver(),
            GetDefaultFileSystemManager(),
            GetDefaultLogger())
        {
        }

        // ************* Helper Functions ************************************************************************************************
        // **************************************************
        // Function: Gets the default project path
        // Description: Creates an IProjectPathResolver
        // Notes: Used in the constructor
        private static IProjectPathResolver GetDefaultProjectPathResolver()
        {
            return new DefaultProjectPathResolver();
        }

        // **************************************************
        // Function: Gets the default file system manager
        // Description: Creates an IFileSystemManager
        // Notes: Used in the constructor
        private static IFileSystemManager GetDefaultFileSystemManager()
        {
            return new StandardFileSystemManager();
        }

        // **************************************************
        // Function: Gets the default logger
        // Description: Creates an ILogger<MainWindow>
        // Notes: Used in the constructor
        private static ILogger<MainWindow> GetDefaultLogger()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information); // 'Information' is the most verbose setting
            });
            return loggerFactory.CreateLogger<MainWindow>();
        }

        // **************************************************
        // Function: Gets the project root
        // Description: Returns the root's file path as a string
        // Notes: Implemented in DefaultProjectPathResolver.cs
        private string GetProjectRoot()
        {
            return _pathResolver.ResolveProjectRoot();
        }

        // **************************************************
        // Function: Gets the yolo script
        // Description: Returns the yolo script's file path as a string
        // Notes:
        //      -Implemented in DefaultProjectPathResolver.cs
        //      -Made this its own function because it's more important
        //       than other 'get file path' functions. For others, you
        //       can use GetPath(string subdirectory)
        private string GetYoloScriptDirectory()
        {
            return _pathResolver.ResolveYoloScriptPath();
        }

        // **************************************************
        // Function: Gets the csv script
        // Description: Returns the csv script's file path as a string
        // Notes:
        //      -Implemented in DefaultProjectPathResolver.cs
        //      -Made this its own function because it's more important
        //       than other 'get file path' functions. For others, you
        //       can use GetPath(string subdirectory)
        private string GetCsvScriptDirectory()
        {
            return _pathResolver.ResolveCsvScriptDirectory();
        }

        // **************************************************
        // Function: Gets the source folder path
        //           (Used in OpenFolderClick(object sender, RoutedEventArgs e))
        // Description: Returns the user-selected file path as a string
        // Notes: Implemented in DefaultProjectPathResolver.cs
        private string GetSourceFolderPath()
        {
            return _pathResolver.ResolveSourceFolder();
        }

        // **************************************************
        // Function: Gets a path
        // Description: Gets the folder path root/subdirectory and returns it as a string
        // Notes: Implemented in DefaultProjectPathResolver.cs
        private string GetPath(string subdirectory)
        {
            return _pathResolver.ResolvePath(subdirectory);
        }

        // **************************************************
        // Function: Makes a Directory if it Doesn't Already Exist
        private void MakeDirectoryIfNotExists(string directory)
        {
            if (!Directory.Exists(directory))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
                catch (System.UnauthorizedAccessException ex)
                {
                    _logger.LogError("Permission denied creating directory", ex);
                    HandleDirectoryCreationError("InsufficientPermissions");

                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to create directory", ex);
                    HandleDirectoryCreationError(ex.Message);
                }
            }
        }

        private void HandleDirectoryCreationError(string errorMessage)
        {
            MessageBox.Show(
                $"Cannot create directory: {errorMessage}",
                "Directory Creation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        // *******************************************************************************************************************************

        // ************* Page Navigation *************************************************************************************************
        // **************************************************
        // Function: Home Page Button Click
        // Description: Navigates to the home page
        private void HomeButtonClick(object sender, RoutedEventArgs e)
        {
        }

        // **************************************************
        // Function: History Page Button Click
        // Description: Navigates to the history page
        private void HistoryButtonClick(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(new History());
        }

        // **************************************************
        // Function: Settings Page Button Click
        // Description: Navigates to the settings page
        private void SettingsButtonClick(object sender, RoutedEventArgs e)
        {

            MainFrame.Navigate(new Settings());
        }
        // *******************************************************************************************************************************

        // ************* Run Yolo and Collects Data **************************************************************************************
        // **************************************************
        // Function: Runs YOLO
        // Description: Runs the python script stored in the yolo script directory
        // Notes: Writing credit to Aden Ratliff
        private void RunYolo()
        {
            ProcessStartInfo start = new ProcessStartInfo();

            string yoloScriptDirectory = GetYoloScriptDirectory();

            start.FileName = "python";
            string sampleDataPath = System.IO.Path.Combine(GetProjectRoot(), "sample_data");   //FishLens/sample_data
            start.Arguments = $"\"{yoloScriptDirectory}\" \"{sampleDataPath}\""; //argv[1] = sample_data
            start.RedirectStandardOutput = true; //Comment out to supress Python output
            start.RedirectStandardError = true; //Comment out to supress Python errors

            // Run Script
            start.UseShellExecute = false;
            try
            {
                Process process = Process.Start(start);
                string output = process.StandardOutput.ReadToEnd(); //Error handling
                string error = process.StandardError.ReadToEnd();   //Error handling
                process.WaitForExit();
                MessageBox.Show($"Output:\n{output}\n\nErrors:\n{error}");
            }

            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Could not process videos.", MessageBoxButton.OK);
            }
        }
        // ******************************************************************************************************************************************

        // ************* Open Folder and Put Data Into Database *************************************************************************************
        // **************************************************
        // Function: Opens Folder on Click
        // Description: An OpenFolderDialog appears when the 'Open Folder' button is clicked
        private void OpenFolderClick(object sender, RoutedEventArgs e)
        {
            string sourceFolderPath = GetSourceFolderPath();
            if (string.IsNullOrEmpty(sourceFolderPath)) return;

            // Determine Save Directory
            var projectRoot = GetProjectRoot();
            string saveDirectory = System.IO.Path.Combine(projectRoot, "SavedVids");    //FishLens/SavedVids

            ProcessVideos(sourceFolderPath, saveDirectory);

            // Make the export button visible
            exportData.Visibility = Visibility.Visible;
        }

        // **************************************************
        // Function: Processes Videos
        // Description: Calls a series of functions to process the videos held in the directory
        //              opened by OpenFolderClick(object sender, RoutedEventArgs e)
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
        // Function: Creates a Sorted List of Videos
        // Description: Gets the confidence rating of each video and list of all videos
        //              sorted from lowest to highest confidence
        private List<(FileInfo vid, Video data)> CreateSortedListOfVideos(string directory)
        {
            // Get each saved video's name and create a list to sort by confidence
            DirectoryInfo vidsInfo = new DirectoryInfo(directory);
            FileInfo[] fileInfos = vidsInfo.GetFiles("*");

            // Create a list to hold video data with confidence for sorting
            List<(FileInfo vid, Video data)> videoDataList = new List<(FileInfo, Video)>();

            foreach (FileInfo vid in fileInfos)
            {
                // Get only mp4 and asf files
                string extension = vid.Extension.ToLower();
                if (extension != ".mp4" && extension != ".asf") continue;

                Video data = GetData(vid.Name);
                videoDataList.Add((vid, data));
            }

            // Sort by confidence (least to most)
            videoDataList = videoDataList.OrderBy(x => x.data.avgConfidence).ToList();
            return videoDataList;
        }

        // **************************************************
        // Function: Gets the Video Data
        // Description: Looks in the CSV file for the row associated with the video's title,
        //              and creates and returns a Video object with that row's data
        private Video GetData(string videoFileName)
        {
            Video vid = new Video();

            string csvPath = GetCsvScriptDirectory();

            // Check if the CSV file exists
            if (!File.Exists(csvPath))
            {
                MessageBox.Show("Analysis data file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return vid;
            }

            try
            {
                vid = GetVideoFileValues(vid, csvPath, videoFileName);
                return vid;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading analysis data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return vid;
        }

        // **************************************************
        // Function: Gets a Video File's Values
        // Description: Parses a CSV row to put data into a Video object
        // Notes: This is a helper function for GetData(string videoFileName)
        private Video GetVideoFileValues(Video vid, string csvPath, string videoFileName)
        {
            string[] lines = File.ReadAllLines(csvPath);

            // Skip header and find the matching video
            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(',');

                // Check if this row matches the video we're looking for
                if (columns[0].Trim() == videoFileName)
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
            }

            // If we get here, the video wasn't found in the CSV
            vid.name = videoFileName;
            vid.trackId = "-1";
            vid.likelyClass = "N/A";
            vid.confidence = "00.00%";
            vid.startTime = "00.00";
            vid.endTime = "00.00";
            vid.avgConfidence = 00.00;
            vid.direction = "Unknown";
            return vid;
        }

        // **************************************************
        // Function: Enters Data into a File
        // Description: Copies data from an input folder to an output directory
        private void EnterDataInFile(string inputFolder, string outputDirectory)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(inputFolder);
            FileInfo[] info = dirInfo.GetFiles("*");
            foreach (FileInfo file in info)
            {
                string fileName = System.IO.Path.GetFileName(file.FullName);
                string destinationPath = System.IO.Path.Combine(outputDirectory, fileName);

                try
                {
                    System.IO.File.Copy(file.FullName, destinationPath, true);
                }
                catch (IOException ex)
                {
                    MessageBox.Show($"Error Saving File: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (SecurityException)
                {
                    MessageBox.Show("Insufficient permissions to copy the file.", "Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        // ******************************************************************************************************************************************

        // ************* Save and Export Data *******************************************************************************************************
        // **************************************************
        // Function: Exports Data on Click
        // Description: Exports data to an Excel sheet when the 'Export Data' button is clicked
        private void ExportDataClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string csvPath = GetCsvScriptDirectory();


                // Check if the CSV file exists
                if (!File.Exists(csvPath))
                {
                    MessageBox.Show("No analysis data found to export.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Let user choose where to save the Excel file
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    DefaultExt = ".xlsx",
                    FileName = $"FishLens_Analysis_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    MakeExcelSheetAndInsertData(saveFileDialog, csvPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // **************************************************
        // Function: Makes an Excel Sheet and Inserts Data
        // Notes: This is a helper function to ExportDataClick(object sender, RoutedEventArgs e)
        private void MakeExcelSheetAndInsertData(SaveFileDialog saveFileDialog, string csvPath)
        {
            string excelPath = saveFileDialog.FileName;

            // Read the CSV file
            string[] lines = File.ReadAllLines(csvPath);

            // Create a new Excel workbook
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Analysis Data");

                // Write the data
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] columns = lines[i].Split(',');
                    for (int j = 0; j < columns.Length; j++)
                    {
                        worksheet.Cell(i + 1, j + 1).Value = columns[j].Trim();
                    }
                }

                // Format header row
                if (lines.Length > 0)
                {
                    var headerRow = worksheet.Row(1);
                    headerRow.Style.Font.Bold = true;
                    headerRow.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
                }

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                // Save the workbook
                workbook.SaveAs(excelPath);
            }

            MessageBox.Show($"Data exported successfully to:\n{excelPath}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);

            // Optionally open the file
            var result = MessageBox.Show("Would you like to open the exported file?", "Open File", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(excelPath) { UseShellExecute = true });
            }
        }

        // **************************************************
        // Function: Save Button
        // Desctiption: Saves any changes that the user makes to the data generated by the AI models
        // TODO: Implement Save Button
        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {

        }
        // ******************************************************************************************************************************************

        // ********************* Display Data to User ***********************************************************************************************
        // **************************************************
        // Function: Video Buttons Functionality
        // Description: Displays a video when its associated button is clicked
        private void VideoButtonClick(object sender, RoutedEventArgs e)
        {
            Button clickedButton = (Button)sender;
            string videoPath = clickedButton.Tag.ToString();
            videoPlayer.Source = new Uri(videoPath);
            videoPlayer.Play();

            string videoFileName = System.IO.Path.GetFileName(videoPath);
            GetData(videoFileName);
        }

        // **************************************************
        // Function: Displays Data to User
        // Description: Displays the data associated with a video after its button has been clicked
        private void DisplayDataInUi(string videoFileName)
        {
            Video vid = GetData(videoFileName);

            // Update the UI elements
            videoName.Text = vid.name;
            videoDateTime.Text = $"Duration: {vid.startTime}s - {vid.endTime}s";
            fishPresentStatus.Text = vid.likelyClass == "fish" ? "Present" : "Not Present";
            fishPresentConfidence.Text = vid.avgConfidence.ToString();
            travelDirection.Text = char.ToUpper(vid.direction[0]) + vid.direction.Substring(1); // Capitalize first letter
        }

        // **************************************************
        // Function: Creates a List of Video Buttons
        // Description: Creates a list of videos and associated data, and adds a button for each and adds it to the sidebar
        private void CreateVideoButtonsList(List<(FileInfo videoFile, Video videoData)> videoDataList)
        {
            // Now create buttons in sorted order
            foreach (var (videoFile, videoData) in videoDataList)
            {
                Button button = CreateSingleVideoButton(videoFile, videoData);
                button.Click += VideoButtonClick;
                videoList.Children.Add(button);
            }
        }

        // **************************************************
        // Function: Creates a Single Video Button
        // Description: Creates and returns a single button associated with a video file and its associated data
        // Notes: This is a helper function for CreateVideoButtonsList(List<(FileInfo videoFile, Video videoData)> videoDataList)
        private Button CreateSingleVideoButton(FileInfo videoFile, Video videoData)
        {
            bool isLowConfidence = videoData.avgConfidence < config.ConfidenceThreshold;
            return new Button
            {
                Content = videoFile.Name,
                Background = isLowConfidence
                    ? new SolidColorBrush(Colors.Red)
                    : new SolidColorBrush(Colors.WhiteSmoke),
                Margin = new Thickness(5),
                Padding = new Thickness(5),
                Height = 40,
                Tag = videoFile.FullName,
            };
        }
        // ******************************************************************************************************************************************
    }
}