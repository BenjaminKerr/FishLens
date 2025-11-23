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
            //  Display
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "MP4 Video|*.mp4|ASF Video|*.asf";
            openFileDialog.Title = "Select a video file for analysis";
            string sourceFilePath = string.Empty;
            if (openFileDialog.ShowDialog() == true)
            {
                sourceFilePath = openFileDialog.FileName;
                VideoPlayer.Source = new Uri(sourceFilePath);
                VideoPlayer.Play();
            }


            // Save

            // Determine Save Directory
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;               //FishLens/FrontEnd/FishLens-App/bin/Debug
            string projectRoot = System.IO.Path.GetDirectoryName(baseDirectory);        //FishLens/FrontEnd/FishLens-App/bin
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot);                 //FishLens/FrontEnd/FishLens-App
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot);                 //FishLens/FrontEnd
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot);                 //FishLens
            projectRoot = System.IO.Path.GetDirectoryName(projectRoot);                 //FishLens      ******One more for some reason?? It works like this????******
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

            string fileName = System.IO.Path.GetFileName(sourceFilePath);
            string destinationPath = System.IO.Path.Combine(saveDirectory, fileName);

            try
            {
                System.IO.File.Copy(sourceFilePath, destinationPath, true);
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
}
