using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        //  Displays and saves videos uploaded by the user.
        private void openFolder_Click(object sender, RoutedEventArgs e)
        {
            //  Display
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "MP4 Video|*.mp4|ASF Video|*.asf";
            if (openFileDialog.ShowDialog() == true)
            {
                VideoPlayer.Source = new Uri(openFileDialog.FileName);
                VideoPlayer.Play();
            }

            // Save
            byte[] videoData = File.ReadAllBytes(openFileDialog.FileName);
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Media Files|*.mp4;*.asf";
            saveFileDialog.InitialDirectory = @"SavedVids";
            if (saveFileDialog.ShowDialog() == true && saveFileDialog.FileName != "")
            {
                using (System.IO.FileStream fs = (System.IO.FileStream)saveFileDialog.OpenFile())
                { 
                    try
                    {
                        fs.Write(videoData, 0, videoData.Length);
                    }
                    catch (IOException ex)
                    {
                        MessageBox.Show($"Error Saving File {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }
}
