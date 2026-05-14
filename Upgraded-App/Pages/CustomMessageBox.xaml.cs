using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FishLens_App
{
    public partial class CustomMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        private CustomMessageBox(string title, string message, MessageBoxButton buttons)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            BuildButtons(buttons);
        }

        private void BuildButtons(MessageBoxButton buttons)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    AddButton("OK", MessageBoxResult.OK, primary: true);
                    break;
                case MessageBoxButton.OKCancel:
                    AddButton("Cancel", MessageBoxResult.Cancel, primary: false);
                    AddButton("OK", MessageBoxResult.OK, primary: true);
                    break;
                case MessageBoxButton.YesNo:
                    AddButton("No", MessageBoxResult.No, primary: false);
                    AddButton("Yes", MessageBoxResult.Yes, primary: true);
                    break;
                case MessageBoxButton.YesNoCancel:
                    AddButton("Cancel", MessageBoxResult.Cancel, primary: false);
                    AddButton("No", MessageBoxResult.No, primary: false);
                    AddButton("Yes", MessageBoxResult.Yes, primary: true);
                    break;
            }
        }

        private void AddButton(string label, MessageBoxResult result, bool primary)
        {
            var btn = new Button
            {
                Content = label,
                MinWidth = 80,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 13,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                BorderThickness = new Thickness(0),
                Background = primary
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["WindowBackground"],
                Foreground = (Brush)Application.Current.Resources["OnAccentForeground"],
            };

            // Rounded corners via template
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(16, 0, 16, 0));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(cp);
            btn.Template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };

            var res = result;
            btn.Click += (s, e) => { Result = res; DialogResult = true; };
            ButtonPanel.Children.Add(btn);
        }

        // ── Static show helpers to match MessageBox.Show signatures ──

        public static MessageBoxResult Show(string message, string title = "",
            MessageBoxButton buttons = MessageBoxButton.OK,
            Window owner = null)
        {
            var dlg = new CustomMessageBox(title, message, buttons);
            dlg.Owner = owner ?? Application.Current.MainWindow;
            dlg.ShowDialog();
            return dlg.Result;
        }
    }
}