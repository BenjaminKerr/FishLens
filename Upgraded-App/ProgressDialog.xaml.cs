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
        public event Action Cancelled;
        private bool _closedByCode = false;

        public ProgressDialog()
        {
            InitializeComponent();
            Closing += (s, e) =>
            {
                if (!_closedByCode)
                    Cancelled?.Invoke();
            };
        }

        public void CloseWithoutCancel()
        {
            Dispatcher.Invoke(() =>
            {
                _closedByCode = true;
                Close();
            });
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

        private int _frameSegmentCount = 0;
        private static readonly SolidColorBrush SegmentFilled = new SolidColorBrush(Color.FromRgb(0x3E, 0x8E, 0xC4));
        private static readonly SolidColorBrush SegmentEmpty  = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

        public void SetFrameBar(int frame, int totalFrames)
        {
            if (totalFrames <= 0) return;
            Dispatcher.Invoke(() =>
            {
                int segCount = Math.Max(1, (int)Math.Round(totalFrames / 100.0));
                if (segCount != _frameSegmentCount)
                {
                    frameSegments.Children.Clear();
                    frameSegments.Columns = segCount;
                    for (int i = 0; i < segCount; i++)
                        frameSegments.Children.Add(new Border
                        {
                            Margin = new Thickness(1.5, 0, 1.5, 0),
                            CornerRadius = new CornerRadius(2),
                            Background = SegmentEmpty
                        });
                    _frameSegmentCount = segCount;
                    progressBar.Visibility = Visibility.Collapsed;
                    frameSegmentContainer.Visibility = Visibility.Visible;
                }
                int filled = Math.Min(frame / 100, segCount);
                for (int i = 0; i < segCount; i++)
                    ((Border)frameSegments.Children[i]).Background =
                        i < filled ? SegmentFilled : SegmentEmpty;
            });
        }

        public void ResetFrameBar()
        {
            Dispatcher.Invoke(() =>
            {
                frameSegments.Children.Clear();
                _frameSegmentCount = 0;
                frameSegmentContainer.Visibility = Visibility.Collapsed;
                progressBar.Visibility = Visibility.Visible;
            });
        }
    }
}
