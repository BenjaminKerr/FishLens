using Microsoft.Win32;
using System;
using System.Collections.Generic;
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
            string projectRoot = System.IO.Path.GetDirectoryName(baseDirectory);        //FishLens/FrontEnd/FishLens-App/bin
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot);                 //FishLens/FrontEnd/FishLens-App
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot);                 //FishLens/FrontEnd
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot);                 //FishLens
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot);                 //FishLens      ******One more for some reason?? It works like this????******
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot);                 //FishLens      ******Two more now!?!?!?******
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
        }
    }
}
