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

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
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

        // ************* Puts Data into Analysis Bar *************
        private void enter_data()
        {
            // TODO: Parse data file here

            videoName.Text = "Video 1";
            videoDateTime.Text = "12/3/2025 17:34";
            fishPresentStatus.Text = "Present";
            fishPresentConfidence.Text = "70%";
            travelDirection.Text = "Upstream";
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

            // Get each saved video's name and make a button on the sidebar for it.
            DirectoryInfo vidsInfo = new DirectoryInfo(saveDirectory);
            FileInfo[] fileInfos = vidsInfo.GetFiles("*");
            foreach (FileInfo vid in fileInfos)
            {
                // Get only mp4 and asf files
                string extension = vid.Extension.ToLower();
                if (extension != ".mp4" && extension != ".asf") continue;

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

        // ************* Display Video Button *************
        // Displays the video associated with a button on the sidebar.
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = (Button)sender;
            string videoPath = clickedButton.Tag.ToString();
            videoPlayer.Source = new Uri(videoPath);
            videoPlayer.Play();

            enter_data();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
