using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for ProgressDialog.xaml
    /// </summary>
    public partial class ProgressDialog : Window
    {
        public ProgressDialog()
        {
            InitializeComponent();
        }

        public void UpdateMessage(string message)
        {
            Dispatcher.Invoke(() => messageText.Text = message);
        }

        public void UpdateProgress(string videoStatus, string frameStatus)
        {
            Dispatcher.Invoke(() =>
            {
                messageText.Text = videoStatus;
                frameText.Text = frameStatus;
            });
        }

        public void SetProgressBar(int value, int max)
        {
            Dispatcher.Invoke(() =>
            {
                if (max > 0)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Maximum = max;
                    progressBar.Value = value;
                }
                else
                {
                    progressBar.IsIndeterminate = true;
                }
            });
        }
    }
}
