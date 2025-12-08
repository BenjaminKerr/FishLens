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

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 

    public class Video
    {
        public string video { get; set; }
        public string trackId { get; set; }
        public string likelyClass { get; set; }
        public string confidence { get; set; }
        public string startTime { get; set; }
        public string endTime { get; set; }
        public double avgConfidence { get; set; }
        public string direction { get; set; }
    }

    public partial class MainWindow : Window
    {
        // This is the confidence threshold that determines if a button is red or not.
        // TO-DO: Make this editable in a settings page.
        public double threshold = 0.7;
        public MainWindow()
        {
            InitializeComponent();
        }

        // ************* Runs Yolo and Collects Data *************
        private void run_yolo()
        {
            ProcessStartInfo start = new ProcessStartInfo();

            // Get script directory
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;               //FishLens/FrontEnd/FishLens-App/bin/Debug
            var projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.Parent.FullName;
            string scriptDirectory = System.IO.Path.Combine(projectRoot, "main.py");       //FishLens/main.py


            start.FileName = "python";
            //start.FileName = System.IO.Path.Combine(projectRoot, "Python", "python.exe");


            string sampleDataPath = System.IO.Path.Combine(projectRoot, "sample_data");   //FishLens/sample_data
            start.Arguments = $"\"{scriptDirectory}\" \"{sampleDataPath}\""; //argv[1] = sample_data
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

        // ************* Gets a video's data in a Video object *************
        private Video get_data(string videoFileName)
        {
            Video vid = new Video();
            // Get the path to yolo_output.csv
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.Parent.FullName;
            string csvPath = System.IO.Path.Combine(projectRoot, "fish_summary.csv");

            // Check if the CSV file exists
            if (!File.Exists(csvPath))
            {
                MessageBox.Show("Analysis data file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return vid;
            }

            try
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
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading analysis data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return vid;
        }

        // ************* Puts Data into Analysis Bar *************
        private void enter_data(string videoFileName)
        {
            Video vid = get_data(videoFileName);

            // Update the UI elements
            videoName.Text = vid.video;
            videoDateTime.Text = $"Duration: {vid.startTime}s - {vid.endTime}s";
            fishPresentStatus.Text = vid.likelyClass == "fish" ? "Present" : "Not Present";
            fishPresentConfidence.Text = vid.avgConfidence.ToString();
            travelDirection.Text = char.ToUpper(vid.direction[0]) + vid.direction.Substring(1); // Capitalize first letter
        }

        // ************* Open Folder Click Function *************
        //  Saves videos uploaded by the user.
        private void openFolder_Click(object sender, RoutedEventArgs e)
        {
            // User opens a folder full of videos
            OpenFolderDialog openFolderDialog = new OpenFolderDialog();
            openFolderDialog.Title = "Select a folder full of video files for analysis";
            string sourceFolderPath = string.Empty;
            if (openFolderDialog.ShowDialog() == true)
            {
                sourceFolderPath = openFolderDialog.FolderName;
            }

            // Save

            // Determine Save Directory
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;               //FishLens/FrontEnd/FishLens-App/bin/Debug
            var projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.FullName; //
            string saveDirectory = System.IO.Path.Combine(projectRoot, "SavedVids");    //FishLens/SavedVids

            // If directory has been deleted, create it.
            if (!Directory.Exists(saveDirectory))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(saveDirectory);
                }
                catch (System.UnauthorizedAccessException)
                {
                    MessageBox.Show(
                    "Cannot create the 'SavedVids' folder due to permission restrictions. Run the application as Administrator, or choose a different save path.",
                    "Permission Denied",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                    $"Fatal Error: Could not create analysis directory. Details: {ex.Message}",
                    "Directory Creation Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            DirectoryInfo dirInfo = new DirectoryInfo(sourceFolderPath);
            FileInfo[] info = dirInfo.GetFiles("*");
            foreach (FileInfo file in info)
            {
                string fileName = System.IO.Path.GetFileName(file.FullName);
                string destinationPath = System.IO.Path.Combine(saveDirectory, fileName);

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

            // Run YOLO on the video folder
            run_yolo();

            // Get each saved video's name and create a list to sort by confidence
            DirectoryInfo vidsInfo = new DirectoryInfo(saveDirectory);
            FileInfo[] fileInfos = vidsInfo.GetFiles("*");

            // Create a list to hold video data with confidence for sorting
            List<(FileInfo vid, Video data)> videoDataList = new List<(FileInfo, Video)>();

            foreach (FileInfo vid in fileInfos)
            {
                // Get only mp4 and asf files
                string extension = vid.Extension.ToLower();
                if (extension != ".mp4" && extension != ".asf") continue;

                Video data = get_data(vid.Name);
                videoDataList.Add((vid, data));
            }

            // Sort by confidence (least to most)
            videoDataList = videoDataList.OrderBy(x => x.data.avgConfidence).ToList();

            // Now create buttons in sorted order
            foreach (var (vid, data) in videoDataList)
            {
                if (data.avgConfidence < threshold)
                {
                    Button button = new Button()
                    {
                        Content = $"{vid.Name}",
                        Margin = new Thickness(5),
                        Padding = new Thickness(5),
                        Background = new SolidColorBrush(Colors.Red),
                        Height = 40,
                        Tag = vid.FullName
                    };

                    button.Click += Button_Click;
                    videoList.Children.Add(button);
                }
                else
                {
                    Button button = new Button()
                    {
                        Content = $"{vid.Name}",
                        Margin = new Thickness(5),
                        Padding = new Thickness(5),
                        Background = new SolidColorBrush(Colors.WhiteSmoke),
                        Height = 40,
                        Tag = vid.FullName
                    };

                    button.Click += Button_Click;
                    videoList.Children.Add(button);
                }
            }

            // Make the export button visible
            exportData.Visibility = Visibility.Visible;
        }


        // ************* Display Video Button *************
        // Displays the video associated with a button on the sidebar.
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = (Button)sender;
            string videoPath = clickedButton.Tag.ToString();
            videoPlayer.Source = new Uri(videoPath);
            videoPlayer.Play();

            string videoFileName = System.IO.Path.GetFileName(videoPath);
            enter_data(videoFileName);
        }

        // ************* Data Export Button *************
        // Saves the csv file as an excel sheet 
        private void exportData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the path to fish_summary.csv
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.Parent.Parent.FullName;
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
