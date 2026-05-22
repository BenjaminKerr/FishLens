namespace FishLens_App.Models
{
    public class AnalysisRunSummary
    {
        public int TotalVideos { get; set; }
        public int PendingVideos { get; set; }
        public int AnalyzedVideos { get; set; }
        public int SkippedVideos { get; set; }
        public int FailedVideos { get; set; }
        public bool Cancelled { get; set; }
    }
}
