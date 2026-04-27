// **************************************************
// ***********************************
// File: Video.cs
// Description: Video Class
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using System;

namespace FishLens_App.Models
{
    // **************************************************
    // Function: Video (class)
    // Description: Represents metadata and analysis results for a single video file
    // **************************************************
    public class Video
    {
        // File name of the video.
        public string Name { get; set; } 
        // Identifier for the detected track associated with the video.
        public string TrackId { get; set; }
        // Most-likely detected class (for example "fish").
        public string LikelyClass { get; set; }
        // Confidence text value as stored in CSV (e.g. "00.00%").
        public string Confidence { get; set; }
        // Start time of the detection window.
        public string StartTime { get; set; }
        // End time of the detection window.
        public string EndTime { get; set; }
        // Average numeric confidence for the video.
        public double AvgConfidence { get; set; }
        // Detected travel direction (for example "upstream" or "downstream").
        public string Direction { get; set; }
        // Species label (if provided separately from likelyClass)
        public string Species { get; set; }
        // Confidence for species label (text as stored in CSV)
        public double SpeciesConfidence { get; set; }
        // Detection date and time (as strings from CSV)
        public string Date { get; set; }
        public string Time { get; set; }
        // Combined detection timestamp (nullable)
        public DateTime? DetectionTimestamp { get; set; }
        // Full video file path as stored in CSV
        public string VideoFilePath { get; set; }
        // Location where the video was recorded
        public string Location { get; set; }
        // Monitoring run where this row was created
        public string Run { get; set; }
    }
}
