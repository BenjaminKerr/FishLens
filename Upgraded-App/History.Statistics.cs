using System;
using System.Collections.Generic;
using FishLens_App.Models;

namespace FishLens_App
{
    public partial class History
    {
        #region Statistics Calculations

        // **************************************************
        // Function: CalculateStatistics
        // Description: Analyzes CSV data and calculates comprehensive statistics
        private ReportStatistics CalculateStatistics(string[] csvLines)
        {
            var stats = InitializeStatistics();
            stats.TotalDetections = csvLines.Length;
            double totalConfidence = 0;
            double totalCorrectness = 0;
            int correctnessCount = 0;
            var uniqueDates = new HashSet<DateTime>();

            foreach (string line in csvLines)
            {
                string[] columns = line.Split(',');

                if (columns.Length < 8)
                    continue;

                // CSV layout (reference):
                // 0: video_file, 1: track_id, 2: image_path, 3: likely_class, 4: confidence,
                // 5: start_time_sec, 6: end_time_sec, 7: avg_confidence, 8: direction,
                // 9: species, 10: species_confidence, 11: date, 12: time

                string videoName = columns.Length > 0 ? columns[0] : string.Empty;
                string species = columns.Length > 9 ? columns[9].Trim() : string.Empty;
                string likelyClass = columns.Length > 3 ? columns[3].Trim() : string.Empty;

                string direction = string.Empty;
                if (columns.Length > 8)
                    direction = columns[8].Trim();
                else if (columns.Length > 7)
                    direction = columns[7].Trim();

                ProcessSpeciesData(stats, species, likelyClass);
                ProcessDirectionData(stats, direction);
                ProcessVideoData(stats, videoName);
                totalConfidence += ProcessConfidenceData(stats, columns);

                DateTime? timestamp = null;
                if (columns.Length > 11)
                {
                    var datePart = columns[11].Trim();
                    var timePart = columns.Length > 12 ? columns[12].Trim() : string.Empty;
                    if (!string.IsNullOrEmpty(timePart))
                    {
                        if (DateTime.TryParse($"{datePart} {timePart}", out DateTime ts))
                            timestamp = ts;
                    }
                    else
                    {
                        if (DateTime.TryParse(datePart, out DateTime ts2))
                            timestamp = ts2;
                    }
                }

                if (timestamp.HasValue)
                {
                    DateTime dateOnly = timestamp.Value.Date;
                    uniqueDates.Add(dateOnly);

                    if (stats.DetectionsByDate.ContainsKey(dateOnly))
                        stats.DetectionsByDate[dateOnly]++;
                    else
                        stats.DetectionsByDate[dateOnly] = 1;

                    ProcessGroupedByDateTime(stats, timestamp.Value, species);

                    if (!stats.MinDetectionTimestamp.HasValue || timestamp.Value < stats.MinDetectionTimestamp.Value)
                        stats.MinDetectionTimestamp = timestamp.Value;
                    if (!stats.MaxDetectionTimestamp.HasValue || timestamp.Value > stats.MaxDetectionTimestamp.Value)
                        stats.MaxDetectionTimestamp = timestamp.Value;

                    int hr = timestamp.Value.Hour;
                    if (stats.DetectionsByHour.ContainsKey(hr)) stats.DetectionsByHour[hr]++; else stats.DetectionsByHour[hr] = 1;
                }

                string location = ExtractLocationFromVideo(videoName);
                ProcessLocationData(stats, location, species);

                if (columns.Length > 8 && double.TryParse(columns[8], out double correctness))
                {
                    totalCorrectness += correctness;
                    correctnessCount++;
                }
            }

            stats.AverageConfidence = CalculateAverageConfidence(totalConfidence, stats.TotalDetections);
            stats.AverageCorrectness = correctnessCount > 0 ? totalCorrectness / correctnessCount : 0;
            stats.FishPerDay = uniqueDates.Count > 0 ? (double)stats.FishCount / uniqueDates.Count : 0;
            stats.EstimatedUpstreamCount = CalculateEstimatedUpstreamCount(stats);
            stats.AverageLengthCm = 0; // To be implemented when length data is available

            return stats;
        }

        // **************************************************
        // Function: InitializeStatistics
        // Description: Creates and initializes a new ReportStatistics object
        private ReportStatistics InitializeStatistics()
        {
            return new ReportStatistics
            {
                VideoDetections = new Dictionary<string, int>(),
                SpeciesBreakdown = new Dictionary<string, int>(),
                DetectionsByHour = new Dictionary<int, int>(),
                DetectionsByDate = new Dictionary<DateTime, int>(),
                DetectionsByLocation = new Dictionary<string, int>(),
                GroupedBySpecies = new Dictionary<string, Dictionary<string, int>>(),
                GroupedByDateTime = new Dictionary<DateTime, Dictionary<string, int>>(),
                GroupedByLocation = new Dictionary<string, Dictionary<string, int>>()
            };
        }

        // **************************************************
        // Function: ProcessSpeciesData
        // Description: Updates statistics with species information from a data row
        private void ProcessSpeciesData(ReportStatistics stats, string species, string likelyClass)
        {
            if (!string.IsNullOrEmpty(likelyClass))
            {
                if (likelyClass.Equals("fish", StringComparison.OrdinalIgnoreCase))
                    stats.FishCount++;
                else if (likelyClass.Equals("bird", StringComparison.OrdinalIgnoreCase))
                    stats.BirdCount++;
            }
            else
            {
                if (species.Equals("fish", StringComparison.OrdinalIgnoreCase))
                    stats.FishCount++;
                else if (species.Equals("bird", StringComparison.OrdinalIgnoreCase))
                    stats.BirdCount++;
            }

            if (stats.SpeciesBreakdown.ContainsKey(species))
                stats.SpeciesBreakdown[species]++;
            else
                stats.SpeciesBreakdown[species] = 1;
        }

        // **************************************************
        // Function: ProcessDirectionData
        // Description: Updates statistics with direction information from a data row
        private void ProcessDirectionData(ReportStatistics stats, string direction)
        {
            if (direction.Contains("upstream", StringComparison.OrdinalIgnoreCase))
                stats.UpstreamCount++;
            else if (direction.Contains("downstream", StringComparison.OrdinalIgnoreCase))
                stats.DownstreamCount++;
        }

        // **************************************************
        // Function: ProcessVideoData
        // Description: Updates statistics with video detection counts
        private void ProcessVideoData(ReportStatistics stats, string videoName)
        {
            if (stats.VideoDetections.ContainsKey(videoName))
                stats.VideoDetections[videoName]++;
            else
                stats.VideoDetections[videoName] = 1;
        }

        // **************************************************
        // Function: ProcessConfidenceData
        // Description: Updates confidence statistics and returns the normalized confidence value
        private double ProcessConfidenceData(ReportStatistics stats, string[] columns)
        {
            if (columns.Length > 7)
            {
                var raw = columns[7].Trim().TrimEnd('%');
                if (double.TryParse(raw, out double confidence))
                {
                    if (confidence > 1)
                        confidence = confidence / 100.0;

                    if (confidence >= 0.8)
                        stats.HighConfidenceCount++;

                    return confidence;
                }
            }

            return 0;
        }

        // **************************************************
        // Function: CalculateAverageConfidence
        // Description: Calculates average confidence from total and count
        private double CalculateAverageConfidence(double totalConfidence, int totalDetections)
        {
            return totalDetections > 0 ? totalConfidence / totalDetections : 0;
        }

        // **************************************************
        // Function: ProcessGroupedByDateTime
        // Description: Groups detections by date and species for time-based analysis
        private void ProcessGroupedByDateTime(ReportStatistics stats, DateTime timestamp, string species)
        {
            if (stats.GroupedByDateTime.ContainsKey(timestamp.Date))
            {
                if (stats.GroupedByDateTime[timestamp.Date].ContainsKey(species))
                    stats.GroupedByDateTime[timestamp.Date][species]++;
                else
                    stats.GroupedByDateTime[timestamp.Date][species] = 1;
            }
            else
            {
                stats.GroupedByDateTime[timestamp.Date] = new Dictionary<string, int> { { species, 1 } };
            }
        }

        // **************************************************
        // Function: ProcessLocationData
        // Description: Updates statistics with location-based grouping
        private void ProcessLocationData(ReportStatistics stats, string location, string species)
        {
            if (stats.DetectionsByLocation.ContainsKey(location))
                stats.DetectionsByLocation[location]++;
            else
                stats.DetectionsByLocation[location] = 1;

            if (stats.GroupedByLocation.ContainsKey(location))
            {
                if (stats.GroupedByLocation[location].ContainsKey(species))
                    stats.GroupedByLocation[location][species]++;
                else
                    stats.GroupedByLocation[location][species] = 1;
            }
            else
            {
                stats.GroupedByLocation[location] = new Dictionary<string, int> { { species, 1 } };
            }
        }

        // **************************************************
        // Function: ExtractLocationFromVideo
        // Description: Extracts location name from video filename or path
        private string ExtractLocationFromVideo(string videoName)
        {
            string lower = videoName.ToLower();

            if (lower.Contains("keno"))
                return "Keno Dam";
            if (lower.Contains("link") || lower.Contains("river"))
                return "Link River Dam";
            if (lower.Contains("spencer"))
                return "Spencer Creek";

            return "Unknown Location";
        }

        // **************************************************
        // Function: CalculateEstimatedUpstreamCount
        // Description: Estimates net upstream count (upstream minus downstream)
        private double CalculateEstimatedUpstreamCount(ReportStatistics stats)
        {
            return stats.UpstreamCount - stats.DownstreamCount;
        }

        // **************************************************
        // Function: CalculatePercentage
        // Description: Calculates percentage value for reporting
        private double CalculatePercentage(int value, int total)
        {
            return total > 0 ? (value * 100.0 / total) : 0;
        }

        #endregion
    }
}