using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
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

        public MainWindow(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            InitializeComponent();
        }

        public MainWindow() : this(
            GetDefaultProjectPathResolver(),
            GetDefaultFileSystemManager(),
            GetDefaultLogger())
        {
        }

        // ************* Helper Functions ************************************************************************************************
        private static IProjectPathResolver GetDefaultProjectPathResolver()
        {
            return new DefaultProjectPathResolver();
        }
        private static IFileSystemManager GetDefaultFileSystemManager()
        {
            return new StandardFileSystemManager();
        }
        private static ILogger<MainWindow> GetDefaultLogger()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
            });
            return loggerFactory.CreateLogger<MainWindow>();
        }
        private string GetProjectRoot()
        {
            return _pathResolver.ResolveProjectRoot();
        }

        private string GetYoloScriptDirectory()
        {
            return _pathResolver.ResolveYoloScriptPath();
        }
        // *******************************************************************************************************************************





        // ************* Runs Yolo and Collects Data *************************************************************************************
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
        //  Saves videos uploaded by the user.
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
        
        private string GetSourceFolderPath()
        {
            // User opens a folder full of videos
            OpenFolderDialog openFolderDialog = new OpenFolderDialog();
            openFolderDialog.Title = "Select a folder full of video files for analysis";
            string sourceFolderPath = string.Empty;
            if (openFolderDialog.ShowDialog() == true)
            {
                sourceFolderPath = openFolderDialog.FolderName;
            }
            return sourceFolderPath;
        }

        private void ProcessVideos(string inputFolder, string outputDirectory)
        {
            MakeDirectoryIfNotExists(outputDirectory);
            DisplayDataInUi(outputDirectory);
            EnterDataInFile(inputFolder, outputDirectory);
            RunYolo();
            List<(FileInfo vid, Video data)> videoDataList = CreateSortedListOfVideos(outputDirectory);
            CreateVideoButtonsList(videoDataList);
        }

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

        private Video GetData(string videoFileName)
        {
            Video vid = new Video();

            var projectRoot = GetProjectRoot();
            string csvPath = System.IO.Path.Combine(projectRoot, "fish_summary.csv");

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
        private Video GetVideoFileValues(Video vid, string csvPath, string videoFileName)
        {
            // Read all lines from the CSV
            string[] lines = File.ReadAllLines(csvPath);

            // Skip header and find the matching video
            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(',');

                // Check if this row matches the video we're looking for
                if (columns[0].Trim() == videoFileName)
                {
                    // Parse the data
                    vid.video = columns[0].Trim();
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
            vid.video = videoFileName;
            vid.trackId = "-1";
            vid.likelyClass = "N/A";
            vid.confidence = "00.00%";
            vid.startTime = "00.00";
            vid.endTime = "00.00";
            vid.avgConfidence = 00.00;
            vid.direction = "Unknown";
            return vid;
        }

        private void MakeDirectoryIfNotExists(string directory)
        {
            // If directory has been deleted, create it.
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
        private void ExportDataClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var projectRoot = GetProjectRoot();
                string csvPath = System.IO.Path.Combine(projectRoot, "fish_summary.csv");


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

        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {

        }
        // ******************************************************************************************************************************************





        // ********************* Display Data to User ***********************************************************************************************
        private void VideoButtonClick(object sender, RoutedEventArgs e)
        {
            Button clickedButton = (Button)sender;
            string videoPath = clickedButton.Tag.ToString();
            videoPlayer.Source = new Uri(videoPath);
            videoPlayer.Play();

            string videoFileName = System.IO.Path.GetFileName(videoPath);
            GetData(videoFileName);
        }
        private void DisplayDataInUi(string videoFileName)
        {
            Video vid = GetData(videoFileName);

            // Update the UI elements
            videoName.Text = vid.video;
            videoDateTime.Text = $"Duration: {vid.startTime}s - {vid.endTime}s";
            fishPresentStatus.Text = vid.likelyClass == "fish" ? "Present" : "Not Present";
            fishPresentConfidence.Text = vid.avgConfidence.ToString();
            travelDirection.Text = char.ToUpper(vid.direction[0]) + vid.direction.Substring(1); // Capitalize first letter
        }

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
        // ******************************************************************************************************************************************
    }
}
