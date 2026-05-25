using System.Windows;

namespace FishLens_App.Interfaces
{
    public interface IUiDialogService
    {
        MessageBoxResult ShowMessage(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon);

        /// <summary>
        /// Shows a SaveFileDialog and returns the selected file path or null if cancelled.
        /// </summary>
        string ShowSaveFileDialog(string filter, string defaultExt, string fileName);

        /// <summary>
        /// Confirmation helper returning true for Yes, false otherwise.
        /// </summary>
        bool Confirm(string message, string caption);

        /// <summary>
        /// Opens the given path using the OS shell (Process.Start wrapper).
        /// </summary>
        void OpenFile(string path);
    }
}
