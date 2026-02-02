namespace FishLens_App.Models
{
    public class AppConfiguration
    {
        public double ConfidenceThreshold { get; set; } = 0.7;
        public bool HighContrastMode { get; set; }
        public bool LargeText { get; set; }
    }
}
