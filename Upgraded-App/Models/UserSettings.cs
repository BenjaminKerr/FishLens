namespace FishLens_App.Models
{
    public class UserSettings
    {
        public double ConfidenceThreshold { get; set; } = 0.7;
        public bool OutputBox { get; set; }
        public bool ErrorBox { get; set; }
        public bool HighContrastMode { get; set; }
        public bool LargeText { get; set; }
    }
}