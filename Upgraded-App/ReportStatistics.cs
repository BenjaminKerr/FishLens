using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    public class ReportStatistics
    {
        // Existing properties
        public int TotalDetections { get; set; }
        public int FishCount { get; set; }
        public int BirdCount { get; set; }
        public int UpstreamCount { get; set; }
        public int DownstreamCount { get; set; }
        public int HighConfidenceCount { get; set; }
        public double AverageConfidence { get; set; }
        public Dictionary<string, int> VideoDetections { get; set; }
        public Dictionary<string, int> SpeciesBreakdown { get; set; }
        public Dictionary<int, int> DetectionsByHour { get; set; }
        public Dictionary<DateTime, int> DetectionsByDate { get; set; }
        public Dictionary<string, int> DetectionsByLocation { get; set; }
        public double FishPerDay { get; set; }
        public double EstimatedUpstreamCount { get; set; }
        public double AverageCorrectness { get; set; }
        public double AverageLengthCm { get; set; } // Placeholder
        public Dictionary<string, Dictionary<string, int>> GroupedBySpecies { get; set; }
        public Dictionary<DateTime, Dictionary<string, int>> GroupedByDateTime { get; set; }
        public Dictionary<string, Dictionary<string, int>> GroupedByLocation { get; set; }
    }
}
