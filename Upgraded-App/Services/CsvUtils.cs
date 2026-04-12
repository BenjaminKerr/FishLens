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
                if (!string.Equals(Path.GetFileName(columns[0].Trim()), videoFileName, StringComparison.OrdinalIgnoreCase))
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

            // REMOVE THESE IF WORKING
            // THIS NEEDS TO RUN MAIN YOLO BEFORE READING FROM CSV - currently it just reads the already
            // filled out csv which makes it not provide the data for the correct videos
            
            var lines = File.ReadAllLines(csvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length == 0) continue;
                if (string.Equals(Path.GetFileName(cols[0].Trim()), videoFileName, StringComparison.OrdinalIgnoreCase))
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
                if (cols.Length > 0 && string.Equals(Path.GetFileName(cols[0].Trim()), videoFileName, StringComparison.OrdinalIgnoreCase))
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
        // 0: video_file, 1: location, 2: species, 3: species_confidence, 4: likely_class,
        // 5: confidence, 6: direction, 7: start_time_sec, 8: end_time_sec, 9: video_timestamp
        public static Video ParseVideoFromColumns(string[] columns)
        {
            var video = new Video();
            // col 0: full video file path
            video.VideoFilePath = columns.Length > 0 ? columns[0].Trim() : string.Empty;
            video.Name = !string.IsNullOrEmpty(video.VideoFilePath)
                ? Path.GetFileName(video.VideoFilePath)
                : string.Empty;
            video.TrackId = "-1";

            // col 1: location
            video.Location = columns.Length > 1 ? columns[1].Trim() : string.Empty;

            // col 2: species
            video.Species = columns.Length > 2 ? columns[2].Trim() : string.Empty;

            // col 3: species_confidence
            if (columns.Length > 3)
            {
                var speciesConfStr = columns[3].Trim().TrimEnd('%');
                if (double.TryParse(speciesConfStr, out var speciesConfValue))
                {
                    if (speciesConfValue > 1) speciesConfValue = speciesConfValue / 100.0;
                    video.SpeciesConfidence = speciesConfValue;
                }
                else
                {
                    video.SpeciesConfidence = 0.0;
                }
            }
            else
            {
                video.SpeciesConfidence = 0.0;
            }

            // col 4: likely_class
            video.LikelyClass = columns.Length > 4 ? columns[4].Trim() : "N/A";

            // col 5: confidence (also used as display confidence)
            video.Confidence = columns.Length > 5 ? columns[5].Trim() : "00.00%";
            video.AvgConfidence = 0.0;
            if (columns.Length > 5)
            {
                var raw = columns[5].Trim().TrimEnd('%');
                if (double.TryParse(raw, out var d))
                {
                    if (d > 1) d = d / 100.0;
                    video.AvgConfidence = d;
                }
            }

            // col 6: direction
            video.Direction = columns.Length > 6 ? columns[6].Trim() : "Unknown";

            // col 7: start_time_sec
            video.StartTime = columns.Length > 7 ? columns[7].Trim() : "00.00";

            // col 8: end_time_sec
            video.EndTime = columns.Length > 8 ? columns[8].Trim() : "00.00";

            // col 9: video_timestamp (full datetime string e.g. "2025/10/01 19:07:44")
            string videoTimestamp = columns.Length > 9 ? columns[9].Trim() : string.Empty;
            video.Date = videoTimestamp;
            video.Time = string.Empty;
            if (!string.IsNullOrEmpty(videoTimestamp))
            {
                if (DateTime.TryParse(videoTimestamp, out var ts))
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
                SpeciesConfidence = 0.0
            };
        }

        // **************************************************
        // Function: ReadLocationFromNoFishCsv
        // Description: Returns the location string for a video from no_fish_summary.csv,
        //              or null if the video is not present in that file.
        // **************************************************
        public static string ReadLocationFromNoFishCsv(string noFishCsvPath, string videoFileName)
        {
            if (!File.Exists(noFishCsvPath)) return null;
            var lines = File.ReadAllLines(noFishCsvPath);
            // Header: video_file,location,video_timestamp
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length < 2) continue;
                if (string.Equals(Path.GetFileName(cols[0].Trim()), videoFileName, StringComparison.OrdinalIgnoreCase))
                    return cols[1].Trim();
            }
            return null;
        }
    }
}
