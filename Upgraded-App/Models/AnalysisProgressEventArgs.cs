using System;

namespace FishLens_App.Models
{
    public class AnalysisProgressEventArgs : EventArgs
    {
        public string EventType { get; set; } = string.Empty;
        public int TotalVideos { get; set; }
        public int CompletedVideos { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FrameInfo { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
