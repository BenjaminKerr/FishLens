using FishLens_App.Models;
using System;
using System.Data.SqlClient;
using System.IO;

namespace FishLens_App.Services
{
    public static class DbSyncService
    {
        // **************************************************
        // Function: SyncRunToDb
        // Description: Reads every fish track from a run's CSV and upserts each row into
        //              FishDetections. Safe to call repeatedly — existing rows are updated in place.
        // **************************************************
        public static void SyncRunToDb(string csvPath, int orgId, int userId, string connectionString)
        {
            if (File.Exists(csvPath))
            {


                var lines = File.ReadAllLines(csvPath);

                using var conn = new SqlConnection(connectionString);
                conn.Open();

                for (int i = 1; i < lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        var cols = CsvUtils.ParseCsvLine(lines[i]);
                        if (cols.Length >= 2)
                        {
                            var video = CsvUtils.ParseVideoFromColumns(cols);
                            UpsertDetection(conn, video, orgId, userId);
                        }
                    }
                }
            }
        }

        // **************************************************
        // Function: UpsertTrackToDb
        // Description: Upserts a single fish track into FishDetections.
        //              Used after a user manually saves edits so corrections reach the DB.
        // **************************************************
        public static void UpsertTrackToDb(Video track, int orgId, int userId, string connectionString)
        {
            if (track != null)
            {

                using var conn = new SqlConnection(connectionString);
                conn.Open();
                UpsertDetection(conn, track, orgId, userId);
            }
        }

        // **************************************************
        // Function: SyncNoFishRunToDb
        // Description: Reads every no-fish video from a run's session_no_fish.csv and upserts each
        //              row into FishDetections with likely_class = "no_fish" so reports can count
        //              videos where nothing was detected. Safe to call repeatedly.
        // **************************************************
        public static void SyncNoFishRunToDb(string noFishCsvPath, string runName, int orgId, int userId, string connectionString)
        {
            if (File.Exists(noFishCsvPath))
            {
                var lines = File.ReadAllLines(noFishCsvPath);

                using var conn = new SqlConnection(connectionString);
                conn.Open();

                for (int i = 1; i < lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        var cols = lines[i].Split(',');
                        if (cols.Length >= 2)
                        {
                            DateTime? ts = null;
                            if (cols.Length >= 3 && !string.IsNullOrWhiteSpace(cols[2]))
                                ts = DateTime.TryParse(cols[2].Trim(), out var parsed) ? parsed : (DateTime?)null;

                            var video = new Video
                            {
                                VideoFilePath      = cols[0].Trim(),
                                Location           = cols[1].Trim(),
                                Run                = runName,
                                LikelyClass        = "no_fish",
                                StartTime          = "0",
                                EndTime            = "0",
                                DetectionTimestamp = ts,
                            };
                            UpsertDetection(conn, video, orgId, userId);
                        }
                    }
                }
            }
        }

        // **************************************************
        // Function: BackfillNoFishRuns
        // Description: Scans every run folder under allHistoryDir for a session_no_fish.csv
        //              and syncs any found into FishDetections. Called on History page load so
        //              runs processed before this feature existed are caught up automatically.
        // **************************************************
        public static void BackfillNoFishRuns(string allHistoryDir, int orgId, int userId, string connectionString)
        {
            if (Directory.Exists(allHistoryDir))
            {
                foreach (string runDir in Directory.GetDirectories(allHistoryDir))
                {
                    string runName    = Path.GetFileName(runDir);
                    string noFishPath = Path.Combine(runDir, "session_no_fish.csv");
                    SyncNoFishRunToDb(noFishPath, runName, orgId, userId, connectionString);
                }
            }
        }

        private static void UpsertDetection(SqlConnection conn, Video video, int orgId, int userId)
        {
            using var cmd = new SqlCommand("kaharra.UpsertFishDetection", conn);
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue("@pOrgId",              orgId);
            cmd.Parameters.AddWithValue("@pRunName",            (object)video.Run          ?? string.Empty);
            cmd.Parameters.AddWithValue("@pLocationName",       (object)video.Location     ?? "Unknown");
            cmd.Parameters.AddWithValue("@pVideoFile",          (object)video.VideoFilePath ?? video.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("@pSpecies",            string.IsNullOrEmpty(video.Species) ? DBNull.Value : (object)video.Species);
            cmd.Parameters.AddWithValue("@pSpeciesConfidence",  video.SpeciesConfidence > 0 ? (object)video.SpeciesConfidence : DBNull.Value);
            cmd.Parameters.AddWithValue("@pLikelyClass",        string.IsNullOrEmpty(video.LikelyClass) ? DBNull.Value : (object)video.LikelyClass);
            cmd.Parameters.AddWithValue("@pConfidence",         video.AvgConfidence > 0 ? (object)video.AvgConfidence : DBNull.Value);
            cmd.Parameters.AddWithValue("@pDirection",          string.IsNullOrEmpty(video.Direction) ? DBNull.Value : (object)video.Direction);
            cmd.Parameters.AddWithValue("@pStartTimeSec",       string.IsNullOrEmpty(video.StartTime) ? DBNull.Value : (object)video.StartTime);
            cmd.Parameters.AddWithValue("@pEndTimeSec",         string.IsNullOrEmpty(video.EndTime) ? DBNull.Value : (object)video.EndTime);
            cmd.Parameters.AddWithValue("@pDetectionTimestamp", (object)video.DetectionTimestamp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pCreatedByUserId",    userId > 0 ? (object)userId : DBNull.Value);

            cmd.ExecuteNonQuery();
        }
    }
}
