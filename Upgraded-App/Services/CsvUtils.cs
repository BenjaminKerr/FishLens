using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishLens_App.Models;

namespace FishLens_App.Services
{
    public static class CsvUtils
    {
        // **************************************************
        // Function: RemoveVideoFromCsv
        // Description: Removes the CSV row for a given video filename
        // **************************************************
        public static void RemoveVideoFromCsv(string csvPath, string videoFileName)
        {
            var lines = File.ReadAllLines(csvPath).ToList();
            if (lines.Count == 0) return;

            // Keep header
            var header = lines[0];
            var remaining = new List<string> { header };

            for (int i = 1; i < lines.Count; i++)
            {
                var columns = lines[i].Split(',');
                if (columns.Length == 0) continue;
                if (!string.Equals(columns[0].Trim(), videoFileName, StringComparison.OrdinalIgnoreCase))
                {
                    remaining.Add(lines[i]);
                }
            }

            File.WriteAllLines(csvPath, remaining);
        }

        // **************************************************
        // Function: ReadVideoFromCsv
        // Description: Reads a video's row from CSV and returns a Video object
        // **************************************************
        public static Video ReadVideoFromCsv(string csvPath, string videoFileName)
        {
            var vid = new Video();
            if (!File.Exists(csvPath)) return vid;

            var lines = File.ReadAllLines(csvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length == 0) continue;
                if (string.Equals(cols[0].Trim(), videoFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return ParseVideoFromColumns(cols);
                }
            }

            return CreateDefaultVideo(videoFileName);
        }

        // **************************************************
        // Function: UpdateCsvRow
        // Description: Replaces the CSV row for videoFileName with updatedRow
        // **************************************************
        public static void UpdateCsvRow(string csvPath, string videoFileName, string updatedRow)
        {
            var lines = File.ReadAllLines(csvPath).ToList();
            if (lines.Count == 0) return;

            var updatedLines = new List<string> { lines[0] };
            bool found = false;
            for (int i = 1; i < lines.Count; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length > 0 && string.Equals(cols[0].Trim(), videoFileName, StringComparison.OrdinalIgnoreCase))
                {
                    updatedLines.Add(updatedRow);
                    found = true;
                }
                else
                {
                    updatedLines.Add(lines[i]);
                }
            }

            if (!found) throw new InvalidOperationException($"Video {videoFileName} not found in CSV file.");

            File.WriteAllLines(csvPath, updatedLines);
        }

        // **************************************************
        // Function: AppendRows
        // Description: Appends rows to CSV after the header
        // **************************************************
        public static void AppendRows(string csvPath, IEnumerable<string> rows)
        {
            if (!File.Exists(csvPath))
            {
                // If file doesn't exist, create with header and then append
                File.WriteAllLines(csvPath, rows);
                return;
            }

            var lines = File.ReadAllLines(csvPath).ToList();
            // Append to end
            lines.AddRange(rows);
            File.WriteAllLines(csvPath, lines);
        }

        // Parse row columns according to CSV layout:
        // 0: video_file, 1: track_id, 2: image_path, 3: likely_class, 4: confidence,
        // 5: start_time_sec, 6: end_time_sec, 7: avg_confidence, 8: direction,
        // 9: species, 10: species_confidence
        public static Video ParseVideoFromColumns(string[] columns)
        {
            var video = new Video();
            video.Name = columns.Length > 0 ? columns[0].Trim() : string.Empty;
            video.TrackId = columns.Length > 1 ? columns[1].Trim() : "-1";
            video.LikelyClass = columns.Length > 3 ? columns[3].Trim() : "N/A";
            video.Confidence = columns.Length > 4 ? columns[4].Trim() : "00.00%";
            video.StartTime = columns.Length > 5 ? columns[5].Trim() : "00.00";
            video.EndTime = columns.Length > 6 ? columns[6].Trim() : "00.00";

            // avg_confidence may include a '%' suffix or be a decimal; try to parse robustly
            video.AvgConfidence = 0.0;
            if (columns.Length > 7)
            {
                var raw = columns[7].Trim().TrimEnd('%');
                if (double.TryParse(raw, out var d))
                {
                    // If value looks like a percent (e.g., 81 or 81.0), normalize to 0-1 if >1
                    if (d > 1) d = d / 100.0;
                    video.AvgConfidence = d;
                }
            }

            video.Direction = columns.Length > 8 ? columns[8].Trim() : "Unknown";
            video.Species = columns.Length > 9 ? columns[9].Trim() : string.Empty;
            video.SpeciesConfidence = columns.Length > 10 ? columns[10].Trim() : string.Empty;
            video.Date = columns.Length > 11 ? columns[11].Trim() : string.Empty;
            video.Time = columns.Length > 12 ? columns[12].Trim() : string.Empty;

            // Try to parse combined date/time if available
            if (!string.IsNullOrEmpty(video.Date) && !string.IsNullOrEmpty(video.Time))
            {
                if (DateTime.TryParse($"{video.Date} {video.Time}", out var ts))
                    video.DetectionTimestamp = ts;
            }
            return video;
        }

        private static Video CreateDefaultVideo(string videoFileName)
        {
            return new Video
            {
                Name = videoFileName,
                TrackId = "-1",
                LikelyClass = "N/A",
                Confidence = "00.00%",
                StartTime = "00.00",
                EndTime = "00.00",
                AvgConfidence = 0.0,
                Direction = "Unknown",
                Species = string.Empty,
                SpeciesConfidence = string.Empty
            };
        }
    }
}
