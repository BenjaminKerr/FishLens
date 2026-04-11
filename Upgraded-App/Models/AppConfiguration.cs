using System.Collections.Generic;

namespace FishLens_App.Models
{
    public class LocationEntry
    {
        public string Name { get; set; } = "Unknown";
        public string UpstreamDirection { get; set; } = "left";
    }

    public class AppConfiguration
    {
        public double ConfidenceThreshold { get; set; } = 0.7;
        public bool HighContrastMode { get; set; }
        public bool LargeText { get; set; }
        public string ActiveLocation { get; set; } = "Unknown";
        public List<LocationEntry> Locations { get; set; } = new List<LocationEntry>()
        {
            new LocationEntry { Name = "Unknown", UpstreamDirection = "left" }
        };
    }
}
