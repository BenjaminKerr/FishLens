using System.Collections.Generic;

namespace FishLens_App.Models
{
    public class AnalysisBatchContext
    {
        public string VideoFolder { get; set; } = string.Empty;
        public string RunName { get; set; } = string.Empty;
        public string RunFolder { get; set; } = string.Empty;
        public string Location { get; set; } = "Unknown";
        public string UpstreamDirection { get; set; } = "left";
        public bool FastMode { get; set; }
        public string RunCsvPath { get; set; } = string.Empty;
        public string SessionCsvPath { get; set; } = string.Empty;
        public string SessionNoFishCsvPath { get; set; } = string.Empty;
        public string AllHistoryCsvPath { get; set; } = string.Empty;
        public string ImageBatchFolder { get; set; } = string.Empty;
        public string PendingImageFolder { get; set; } = string.Empty;
        public string ClassifiedImageFolder { get; set; } = string.Empty;
        public bool ForceReanalyze { get; set; }
        public List<string> VideoFiles { get; set; } = new List<string>();
    }
}
