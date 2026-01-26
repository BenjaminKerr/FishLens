// **************************************************
// ***********************************
// File: Video.cs
// Description: Video Class
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

namespace FishLens_App
{
    // **************************************************
    // Function: Video (class)
    // Description: Represents metadata and analysis results for a single video file
    // **************************************************
    public class Video
    {
        // File name of the video.
        public string name { get; set; } 
        // Identifier for the detected track associated with the video.
        public string trackId { get; set; }
        // Most-likely detected class (for example "fish").
        public string likelyClass { get; set; }
        // Confidence text value as stored in CSV (e.g. "00.00%").
        public string confidence { get; set; }
        // Start time of the detection window.
        public string startTime { get; set; }
        // End time of the detection window.
        public string endTime { get; set; }
        // Average numeric confidence for the video.
        public double avgConfidence { get; set; }
        // Detected travel direction (for example "upstream" or "downstream").
        public string direction { get; set; }
    }
}
