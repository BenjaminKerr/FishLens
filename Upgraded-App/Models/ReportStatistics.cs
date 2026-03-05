using System;
using System.Collections.Generic;

namespace FishLens_App.Models
{
    public class ReportStatistics
    {
        // Detections overview
        public int TotalDetections { get; set; }
        public Dictionary<string, int> ClassBreakdown { get; set; }

        // Movement patterns
        public int UpstreamCount { get; set; }
        public int DownstreamCount { get; set; }

        // Species breakdown
        public Dictionary<string, int> SpeciesBreakdown { get; set; }

        // Location breakdown
        public Dictionary<string, int> DetectionsByLocation { get; set; }

        // Observations by day
        public Dictionary<DateTime, int> DetectionsByDate { get; set; }

        // Observations by hour
        public Dictionary<int, int> DetectionsByHour { get; set; }

        // Export header only
        public DateTime? MinDetectionTimestamp { get; set; }
        public DateTime? MaxDetectionTimestamp { get; set; }
    }
}