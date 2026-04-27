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
                var columns = ParseCsvLine(lines[i]);
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
        public static Video ReadVideoFromCsv(string csvPath, string videoFileName, string videoFilePath = null, string fallbackRun = "")
        {
            var tracks = ReadAllTracksFromCsv(csvPath, videoFileName, fallbackRun, videoFilePath);
            return tracks.Count > 0 ? tracks[0] : CreateDefaultVideo(videoFileName, fallbackRun);
        }

        // **************************************************
        // Function: ReadAllTracksFromCsv
        // Description: Returns ALL rows for a given video filename as a list of Video objects.
        //              A single video can have multiple tracks (one row per fish detected).
        // **************************************************
        public static List<Video> ReadAllTracksFromCsv(string csvPath, string videoFileName, string fallbackRun = "", string videoFilePath = null)
        {
            var tracks = new List<Video>();
            if (!File.Exists(csvPath)) return tracks;

            var lines = File.ReadAllLines(csvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = ParseCsvLine(lines[i]);
                if (cols.Length == 0) continue;
                if (RowMatchesVideo(cols, videoFileName, videoFilePath))
                {
                    tracks.Add(ParseVideoFromColumns(cols, fallbackRun));
                }
            }

            return tracks.Count > 0 ? tracks : new List<Video> { CreateDefaultVideo(videoFileName, fallbackRun) };
        }

        // **************************************************
        // Function: UpdateCsvRow
        // Description: Replaces the CSV row for videoFileName with updatedRow.
        //              When a video has multiple fish tracks this replaces ALL matching rows -
        //              use UpdateCsvRowForTrack to target a specific track.
        // **************************************************
        public static void UpdateCsvRow(string csvPath, string videoFileName, string updatedRow)
        {
            var lines = File.ReadAllLines(csvPath).ToList();
            if (lines.Count == 0) return;

            var updatedLines = new List<string> { lines[0] };
            bool found = false;
            for (int i = 1; i < lines.Count; i++)
            {
                var cols = ParseCsvLine(lines[i]);
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
        // Function: UpdateCsvRowForTrack
        // Description: Replaces exactly one CSV row - the row for videoFileName whose
        //              start_time_sec column (col 7) matches startTimeSec.
        //              Returns true if a row was found and replaced, false if not present.
        // **************************************************
        public static bool UpdateCsvRowForTrack(string csvPath, string videoFileName, string startTimeSec, string updatedRow, string run = null)
        {
            var lines = File.ReadAllLines(csvPath).ToList();
            if (lines.Count == 0) return false;

            var updatedLines = new List<string> { lines[0] };
            bool found = false;
            for (int i = 1; i < lines.Count; i++)
            {
                var cols = ParseCsvLine(lines[i]);
                bool nameMatch = cols.Length > 0 &&
                    string.Equals(Path.GetFileName(cols[0].Trim()), videoFileName, StringComparison.OrdinalIgnoreCase);
                bool timeMatch = cols.Length > 7 &&
                    string.Equals(cols[7].Trim(), startTimeSec, StringComparison.OrdinalIgnoreCase);
                bool runMatch = string.IsNullOrWhiteSpace(run) ||
                    (cols.Length > 10 && string.Equals(cols[10].Trim(), run, StringComparison.OrdinalIgnoreCase));

                if (nameMatch && timeMatch && runMatch && !found)
                {
                    updatedLines.Add(updatedRow);
                    found = true;
                }
                else
                {
                    updatedLines.Add(lines[i]);
                }
            }

            if (found)
                File.WriteAllLines(csvPath, updatedLines);

            return found;
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
        public static Video ParseVideoFromColumns(string[] columns, string fallbackRun = "")
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
            string videoTimestamp = columns.Length > 9 ? NormalizeTimestampField(columns[9]) : string.Empty;
            video.Date = videoTimestamp;
            video.Time = string.Empty;
            if (!string.IsNullOrEmpty(videoTimestamp))
            {
                if (DateTime.TryParse(videoTimestamp, out var ts))
                    video.DetectionTimestamp = ts;
            }

            // col 10: run attribution
            video.Run = columns.Length > 10 ? columns[10].Trim() : fallbackRun;

            return video;
        }

        private static Video CreateDefaultVideo(string videoFileName, string fallbackRun = "")
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
                SpeciesConfidence = 0.0,
                Run = fallbackRun
            };
        }

        private static bool RowMatchesVideo(string[] columns, string videoFileName, string videoFilePath)
        {
            if (columns.Length == 0) return false;

            string storedPath = columns[0].Trim();
            if (!string.IsNullOrWhiteSpace(videoFilePath) &&
                string.Equals(storedPath, videoFilePath, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(Path.GetFileName(storedPath), videoFileName, StringComparison.OrdinalIgnoreCase);
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
                var cols = ParseCsvLine(lines[i]);
                if (cols.Length < 2) continue;
                if (string.Equals(Path.GetFileName(cols[0].Trim()), videoFileName, StringComparison.OrdinalIgnoreCase))
                    return cols[1].Trim();
            }
            return null;
        }

        public static string[] ParseCsvLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return Array.Empty<string>();

            var values = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];

                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (ch == ',' && !inQuotes)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            values.Add(current.ToString());
            return values.ToArray();
        }

        private static string NormalizeTimestampField(string raw)
        {
            string value = (raw ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                return value;

            if (value.StartsWith("(") && value.EndsWith(")") && value.Contains("',"))
            {
                int firstQuote = value.IndexOf('\'');
                int secondQuote = value.IndexOf('\'', firstQuote + 1);
                if (firstQuote >= 0 && secondQuote > firstQuote)
                    return value.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            }

            if (value.EndsWith("*", StringComparison.Ordinal))
                value = value.TrimEnd('*').Trim();

            return value;
        }
    }
}
