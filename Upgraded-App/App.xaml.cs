using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public CheckBoxToggle CheckBoxes { get; private set; }
        public AppConfiguration Configuration { get; set; }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CheckBoxes = new CheckBoxToggle();
            Configuration = new AppConfiguration();
        }
    }
}
