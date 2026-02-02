using FishLens_App.Models;
using System;
using System.Windows;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Private sets so they aren't accidentally replaced with new instances
        public CheckBoxToggle CheckBoxes { get; private set; }
        public AppConfiguration Configuration { get; private set; }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); // Call the regular startup logic first
            CheckBoxes = new CheckBoxToggle();
            Configuration = new AppConfiguration();
        }
    }
}
