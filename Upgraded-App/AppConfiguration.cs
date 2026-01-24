using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    public class AppConfiguration
    {
        public double ConfidenceThreshold { get; set; } = 0.7;
        public bool AutoPlayVideos { get; set; } = true;
        public string VideoQuality { get; set; } = "Medium";
        public bool HighContrastMode { get; set; } = false;
        public bool LargeText { get; set; } = false;
    }
}
