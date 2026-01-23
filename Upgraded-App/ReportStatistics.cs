using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    public class ReportStatistics
    {
        public int TotalDetections { get; set; }
        public int FishCount { get; set; }
        public int BirdCount { get; set; }
        public int UpstreamCount { get; set; }
        public int DownstreamCount { get; set; }
        public Dictionary<string, int> VideoDetections { get; set; }
        public Dictionary<string, int> SpeciesBreakdown { get; set; }
        public Dictionary<int, int> DetectionsByHour { get; set; }
        public double AverageConfidence { get; set; }
        public int HighConfidenceCount { get; set; }
    }
}
