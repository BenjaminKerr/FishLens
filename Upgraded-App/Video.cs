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
    public class Video
    {
        public string name { get; set; } 
        public string trackId { get; set; }
        public string likelyClass { get; set; }
        public string confidence { get; set; }
        public string startTime { get; set; }
        public string endTime { get; set; }
        public double avgConfidence { get; set; }
        public string direction { get; set; }
    }
}
