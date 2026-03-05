using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishLens_App.Models;

namespace FishLens_App
{
    public partial class History
    {
        #region Statistics Calculations

        // **************************************************
        // Function: CalculateStatistics
        // Description: Parses CSV rows and populates ReportStatistics
        private ReportStatistics CalculateStatistics(string[] csvLines)
        {
            var stats = new ReportStatistics
            {
                SpeciesBreakdown = new Dictionary<string, int>(),
                DetectionsByLocation = new Dictionary<string, int>(),
                DetectionsByDate = new Dictionary<DateTime, int>(),
                DetectionsByHour = new Dictionary<int, int>(),
                ClassBreakdown = new Dictionary<string, int>()
            };

            stats.TotalDetections = csvLines.Length;

            foreach (string line in csvLines)
            {
                string[] col = line.Split(',');
                if (col.Length < 8) continue;

                // CSV layout:
                // 0: date, 1: time, 2: video_file, 3: track_id, 4: image_path, 5: likely_class,
                // 6: confidence, 7: start_time_sec, 8: end_time_sec, 9: avg_confidence,
                // 10: direction, 11: species, 12: species_confidence

                string videoName = col.Length > 2 ? col[2].Trim() : string.Empty;
                string likelyClass = col.Length > 5 ? col[5].Trim() : string.Empty;
                string species = col.Length > 11 ? col[11].Trim() : string.Empty;
                string direction = col.Length > 10 ? col[10].Trim() : string.Empty;

                // "fish 57.0%" -> "fish"
                int spaceIdx = likelyClass.IndexOf(' ');
                if (spaceIdx > 0)
                    likelyClass = likelyClass.Substring(0, spaceIdx);

                // Block 1: class counts

                if (!string.IsNullOrWhiteSpace(likelyClass))
                {
                    if (stats.ClassBreakdown.ContainsKey(likelyClass))
                        stats.ClassBreakdown[likelyClass]++;
                    else
                        stats.ClassBreakdown[likelyClass] = 1;
                }

                // Block 2: direction counts
                if (direction.Contains("upstream", StringComparison.OrdinalIgnoreCase))
                    stats.UpstreamCount++;
                else if (direction.Contains("downstream", StringComparison.OrdinalIgnoreCase))
                    stats.DownstreamCount++;

                // Block 3: species breakdown
                // Use species column if available, fall back to likelyClass
                string speciesKey = !string.IsNullOrWhiteSpace(species) ? species : likelyClass;
                if (!string.IsNullOrWhiteSpace(speciesKey))
                {
                    if (stats.SpeciesBreakdown.ContainsKey(speciesKey))
                        stats.SpeciesBreakdown[speciesKey]++;
                    else
                        stats.SpeciesBreakdown[speciesKey] = 1;
                }

                // Block 4: location
                string location = ExtractLocationFromVideo(videoName);
                if (stats.DetectionsByLocation.ContainsKey(location))
                    stats.DetectionsByLocation[location]++;
                else
                    stats.DetectionsByLocation[location] = 1;

                // Blocks 5 & 6: parse timestamp
                if (col.Length > 0)
                {
                    string datePart = col[0].Trim();
                    string timePart = col.Length > 1 ? col[1].Trim() : string.Empty;
                    string combined = string.IsNullOrEmpty(timePart) ? datePart : $"{datePart} {timePart}";

                    if (DateTime.TryParse(combined, out DateTime ts))
                    {
                        // Block 5: by date
                        DateTime dateOnly = ts.Date;
                        if (stats.DetectionsByDate.ContainsKey(dateOnly))
                            stats.DetectionsByDate[dateOnly]++;
                        else
                            stats.DetectionsByDate[dateOnly] = 1;

                        // Block 6: by hour
                        int hr = ts.Hour;
                        if (stats.DetectionsByHour.ContainsKey(hr))
                            stats.DetectionsByHour[hr]++;
                        else
                            stats.DetectionsByHour[hr] = 1;

                        // Export date range
                        if (!stats.MinDetectionTimestamp.HasValue || ts < stats.MinDetectionTimestamp.Value)
                            stats.MinDetectionTimestamp = ts;
                        if (!stats.MaxDetectionTimestamp.HasValue || ts > stats.MaxDetectionTimestamp.Value)
                            stats.MaxDetectionTimestamp = ts;
                    }
                }
            }

            return stats;
        }

        // **************************************************
        // Function: ExtractLocationFromVideo
        // Description: Maps a video file path to a human-readable location name
        private string ExtractLocationFromVideo(string videoName)
        {
            string fileName = Path.GetFileName(videoName).ToLower();

            if (fileName.Contains("keno")) return "Keno Dam";
            if (fileName.Contains("link") || fileName.Contains("river")) return "Link River Dam";
            if (fileName.Contains("spencer")) return "Spencer Creek";

            return "Unknown Location";
        }

        // **************************************************
        // Function: CalculatePercentage
        private double CalculatePercentage(int value, int total)
            => total > 0 ? value * 100.0 / total : 0;

        #endregion
    }
}