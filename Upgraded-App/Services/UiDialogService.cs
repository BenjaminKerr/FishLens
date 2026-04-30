using FishLens_App.Interfaces;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;

namespace FishLens_App.Services
{
    public class UiDialogService : IUiDialogService
    {
        public MessageBoxResult ShowMessage(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            return MessageBox.Show(message, caption, buttons, icon);
        }

        public string ShowSaveFileDialog(string filter, string defaultExt, string fileName)
        {
            var dlg = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = fileName
            };

            bool? result = dlg.ShowDialog();
            return result == true ? dlg.FileName : null;
        }

        public bool Confirm(string message, string caption)
        {
            var result = MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }

        public void OpenFile(string path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
