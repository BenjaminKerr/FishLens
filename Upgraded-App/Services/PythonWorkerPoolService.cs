using FishLens_App.Interfaces;
using FishLens_App.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FishLens_App.Services
{
    public class PythonWorkerPoolService : IDisposable
    {
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mov", ".mkv", ".asf", ".wmv", ".flv", ".webm" };
        private static readonly string[] FishCsvKeys =
        {
            "video_file", "location", "species", "species_confidence", "likely_class",
            "confidence", "direction", "start_time_sec", "end_time_sec", "video_timestamp", "run"
        };
        private static readonly string[] NoFishCsvKeys = { "video_file", "location", "video_timestamp" };

        private readonly IProjectPathResolver _pathResolver;
        private readonly ILogger _logger;
        private readonly object _workersLock = new object();
        private readonly object _csvLock = new object();
        private readonly List<PythonWorker> _workers = new List<PythonWorker>();
        private readonly string _appSessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        private readonly Timer _idleTimer;
        private CancellationTokenSource _activeRunCts;
        private PythonClassifierProcess _activeClassifier;
        private DateTime _lastWorkCompletedUtc = DateTime.UtcNow;
        private bool _disposed;

        public event EventHandler<AnalysisProgressEventArgs> ProgressChanged;

        public PythonWorkerPoolService(IProjectPathResolver pathResolver, ILogger logger)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _logger = logger;
            _idleTimer = new Timer(_ => TrimIdleWorkers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public Task StartBaselineAsync()
        {
            EnsureWorkerCountStarted(1);
            return Task.CompletedTask;
        }

        public async Task<AnalysisRunSummary> AnalyzeFolderAsync(AnalysisBatchContext context, CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            _activeRunCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _activeRunCts.Token;

            var allVideos = context.VideoFiles?.Count > 0
                ? context.VideoFiles.ToList()
                : Directory.GetFiles(context.VideoFolder)
                    .Where(path => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .ToList();

            var pending = FilterPendingVideos(allVideos, context).ToList();
            var summary = new AnalysisRunSummary
            {
                TotalVideos = allVideos.Count,
                PendingVideos = pending.Count,
                SkippedVideos = allVideos.Count - pending.Count
            };

            RaiseProgress(new AnalysisProgressEventArgs
            {
                EventType = "total",
                TotalVideos = pending.Count,
                CompletedVideos = 0,
                Message = $"Analyzing {pending.Count} video(s)"
            });

            if (pending.Count == 0)
                return summary;

            EnsureCsvFiles(context);
            PrepareImageFolders(context);

            int targetWorkers = WorkerCountPolicy.GetTargetWorkerCount(
                pending.Count,
                Environment.ProcessorCount,
                GetTotalMemoryBytes());

            var workers = EnsureWorkerCountStarted(1);
            var queue = new ConcurrentQueue<string>(pending);
            var completedResults = new ConcurrentBag<PythonVideoResult>();
            int completed = 0;
            int analyzed = 0;
            int failed = 0;

            async Task ConsumeQueueAsync(PythonWorker worker)
            {
                await worker.WaitUntilReadyAsync(token);
                while (!token.IsCancellationRequested && queue.TryDequeue(out string videoPath))
                {
                    int startedNumber = Volatile.Read(ref completed) + 1;
                    string filename = Path.GetFileName(videoPath);
                    RaiseProgress(new AnalysisProgressEventArgs
                    {
                        EventType = "video_started",
                        TotalVideos = pending.Count,
                        CompletedVideos = Math.Min(startedNumber, pending.Count),
                        FileName = filename,
                        Message = $"{worker.ProcessID}|{filename}| Video {Math.Min(startedNumber, pending.Count)}/{pending.Count} - {filename}"
                    });

                    try
                    {
                        var result = await worker.AnalyzeVideoAsync(videoPath, context, token);
                        completedResults.Add(result);
                        Interlocked.Increment(ref analyzed);
                    }
                    catch (OperationCanceledException)
                    {
                        queue.Clear();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        _logger?.LogError(ex, "Failed to analyze {VideoPath}", videoPath);
                    }
                    finally
                    {
                        int done = Interlocked.Increment(ref completed);
                        RaiseProgress(new AnalysisProgressEventArgs
                        {
                            EventType = "video_finished",
                            TotalVideos = pending.Count,
                            CompletedVideos = Math.Min(done, pending.Count),
                            FileName = filename,
                            Message = $"{worker.ProcessID}|{filename}|Completed {Math.Min(done, pending.Count)}/{pending.Count}"
                        });
                    }
                }
            }

            var tasks = new List<Task>
            {
                Task.Run(() => ConsumeQueueAsync(workers[0]), token)
            };

            for (int workerIndex = 2; workerIndex <= targetWorkers; workerIndex++)
            {
                int capturedWorkerIndex = workerIndex;
                tasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(GetWorkerStartupDelay(capturedWorkerIndex), token);
                    if (token.IsCancellationRequested || queue.IsEmpty)
                        return;

                    var stagedWorker = EnsureWorkerCountStarted(capturedWorkerIndex)[capturedWorkerIndex - 1];
                    await ConsumeQueueAsync(stagedWorker);
                }, token));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                summary.Cancelled = true;
            }
            finally
            {
                if (token.IsCancellationRequested)
                    summary.Cancelled = true;
                _lastWorkCompletedUtc = DateTime.UtcNow;
            }

            summary.AnalyzedVideos = analyzed;
            summary.FailedVideos = failed;

            try
            {
                var results = completedResults.ToList();
                if (summary.Cancelled)
                {
                    foreach (var result in results)
                        MarkNoSpecies(result);
                }
                else
                {
                    ShutdownExtraWorkers(keepCount: 1);
                    await ClassifyCompletedResultsAsync(context, results, token);
                }

                foreach (var result in results)
                    WriteResult(context, result);
            }
            finally
            {
                _activeRunCts?.Dispose();
                _activeRunCts = null;
            }

            return summary;
        }

        public void CancelActiveRun()
        {
            _activeRunCts?.Cancel();
            _activeClassifier?.Dispose();
            List<PythonWorker> workers;
            lock (_workersLock)
                workers = _workers.Where(worker => worker.IsBusy).ToList();

            foreach (var worker in workers)
                worker.Kill(restart: false);

            lock (_workersLock)
                _workers.RemoveAll(worker => worker.HasExited);
            EnsureWorkerCountStarted(1);
        }

        public void RestartWorkers()
        {
            ShutdownAllWorkers();
            EnsureWorkerCountStarted(1);
        }

        private List<PythonWorker> EnsureWorkerCountStarted(int targetCount)
        {
            targetCount = Math.Max(1, targetCount);
            lock (_workersLock)
            {
                _workers.RemoveAll(worker => worker.HasExited);
                while (_workers.Count < targetCount)
                {
                    var worker = new PythonWorker(_workers.Count + 1, _pathResolver, _logger, RaiseProgress);
                    _workers.Add(worker);
                    worker.Start();
                }
                return _workers.Take(targetCount).ToList();
            }
        }

        private static TimeSpan GetWorkerStartupDelay(int workerIndex)
        {
            return workerIndex <= 2 ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(8);
        }

        private void PrepareImageFolders(AnalysisBatchContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.ImageBatchFolder))
                return;

            string root = Path.Combine(_pathResolver.ResolveProjectRoot(), "fish_images", "sessions", $"app_{_appSessionId}");
            string batch = Path.Combine(root, $"batch_{DateTime.Now:yyyyMMdd_HHmmss}");
            context.ImageBatchFolder = batch;
            context.PendingImageFolder = Path.Combine(batch, "pending");
            context.ClassifiedImageFolder = Path.Combine(batch, "classified");
            Directory.CreateDirectory(context.PendingImageFolder);
            Directory.CreateDirectory(context.ClassifiedImageFolder);
        }

        private IEnumerable<string> FilterPendingVideos(IEnumerable<string> allVideos, AnalysisBatchContext context)
        {
            if (context.ForceReanalyze || string.IsNullOrWhiteSpace(context.RunCsvPath) || !File.Exists(context.RunCsvPath))
                return allVideos;

            var analyzed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadLines(context.RunCsvPath).Skip(1))
            {
                var columns = CsvUtils.ParseCsvLine(line);
                if (columns.Length > 0 && !string.IsNullOrWhiteSpace(columns[0]))
                    analyzed.Add(Path.GetFullPath(columns[0].Trim()));
            }

            return allVideos.Where(path => !analyzed.Contains(Path.GetFullPath(path)));
        }

        private void WriteResult(AnalysisBatchContext context, PythonVideoResult result)
        {
            if (result == null) return;

            lock (_csvLock)
            {
                if (result.Tracks.Count > 0)
                {
                    var rows = result.Tracks.Select(track => BuildFishCsvRow(track, context.RunName));
                    if (IsDebug(context))
                    {
                        AppendCsvRows(context.RunCsvPath, FishCsvKeys, rows);
                    }
                    else
                    {
                        AppendCsvRows(context.SessionCsvPath, FishCsvKeys, rows);
                        AppendCsvRows(context.RunCsvPath, FishCsvKeys, rows);
                        AppendCsvRows(context.AllHistoryCsvPath, FishCsvKeys, rows);
                    }
                    return;
                }

                if (result.NoFishRow.Count > 0)
                {
                    var slimRow = BuildNoFishCsvRow(result.NoFishRow);
                    var fullRow = BuildNoFishMasterRow(result.NoFishRow, context.RunName);
                    if (IsDebug(context))
                    {
                        AppendCsvRows(context.RunCsvPath, FishCsvKeys, new[] { fullRow });
                    }
                    else
                    {
                        AppendCsvRows(context.SessionNoFishCsvPath, NoFishCsvKeys, new[] { slimRow });
                        AppendCsvRows(context.RunCsvPath, FishCsvKeys, new[] { fullRow });
                        AppendCsvRows(context.AllHistoryCsvPath, FishCsvKeys, new[] { fullRow });
                    }
                }
            }
        }

        private async Task ClassifyCompletedResultsAsync(AnalysisBatchContext context, List<PythonVideoResult> results, CancellationToken token)
        {
            var imagePaths = results
                .SelectMany(result => result.Tracks)
                .Select(track => track.GetValueOrDefault("image_path", string.Empty))
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (imagePaths.Count == 0)
            {
                foreach (var result in results)
                    MarkNoSpecies(result);
                return;
            }

            try
            {
                using var classifier = new PythonClassifierProcess(_pathResolver, _logger);
                _activeClassifier = classifier;
                await classifier.StartAsync(token);
                var classifications = await classifier.ClassifyBatchAsync(imagePaths, context.ClassifiedImageFolder, token);

                foreach (var result in results)
                {
                    foreach (var track in result.Tracks)
                    {
                        string originalPath = track.GetValueOrDefault("image_path", string.Empty);
                        if (classifications.TryGetValue(originalPath, out var classification))
                        {
                            track["species"] = classification.Species;
                            track["species_confidence"] = classification.SpeciesConfidence;
                            track["image_path"] = classification.FinalPath;
                        }
                        else
                        {
                            track["species"] = "No species";
                            track["species_confidence"] = "0.0000";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Species classifier pass failed; writing completed YOLO rows without species.");
                foreach (var result in results)
                    MarkNoSpecies(result);
            }
            finally
            {
                _activeClassifier = null;
            }
        }

        private static void MarkNoSpecies(PythonVideoResult result)
        {
            if (result == null) return;
            foreach (var track in result.Tracks)
            {
                track["species"] = "No species";
                track["species_confidence"] = "0.0000";
            }
        }

        private static bool IsDebug(AnalysisBatchContext context) =>
            string.Equals(context.RunName, "debug", StringComparison.OrdinalIgnoreCase);

        private void EnsureCsvFiles(AnalysisBatchContext context)
        {
            if (IsDebug(context))
            {
                EnsureCsvFile(context.RunCsvPath, FishCsvKeys);
                return;
            }

            EnsureCsvFile(context.SessionCsvPath, FishCsvKeys);
            EnsureCsvFile(context.SessionNoFishCsvPath, NoFishCsvKeys);
            EnsureCsvFile(context.RunCsvPath, FishCsvKeys);
            EnsureCsvFile(context.AllHistoryCsvPath, FishCsvKeys);
        }

        private static void EnsureCsvFile(string path, string[] header)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                File.WriteAllText(path, string.Join(",", header) + Environment.NewLine);
        }

        private static string BuildFishCsvRow(Dictionary<string, string> track, string runName)
        {
            return ToCsvLine(FishCsvKeys.Select(key =>
                key == "run" && !track.ContainsKey(key) ? runName : track.GetValueOrDefault(key, string.Empty)));
        }

        private static string BuildNoFishCsvRow(Dictionary<string, string> row)
        {
            return ToCsvLine(NoFishCsvKeys.Select(key => row.GetValueOrDefault(key, string.Empty)));
        }

        private static string BuildNoFishMasterRow(Dictionary<string, string> row, string runName)
        {
            var values = FishCsvKeys.Select(key =>
            {
                if (key == "video_file") return row.GetValueOrDefault("video_file", string.Empty);
                if (key == "location") return row.GetValueOrDefault("location", string.Empty);
                if (key == "likely_class") return "no_fish";
                if (key == "video_timestamp") return row.GetValueOrDefault("video_timestamp", "Not detected");
                if (key == "run") return row.GetValueOrDefault("run", runName);
                return string.Empty;
            });
            return ToCsvLine(values);
        }

        private static void AppendCsvRows(string path, string[] header, IEnumerable<string> rows)
        {
            EnsureCsvFile(path, header);
            File.AppendAllLines(path, rows);
        }

        private static string ToCsvLine(IEnumerable<string> values)
        {
            return string.Join(",", values.Select(EscapeCsv));
        }

        private static string EscapeCsv(string value)
        {
            value ??= string.Empty;
            if (value.Contains("\""))
                value = value.Replace("\"", "\"\"");
            return value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r")
                ? $"\"{value}\""
                : value;
        }

        private static ulong GetTotalMemoryBytes()
        {
            long total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return total > 0 ? (ulong)total : 8UL * 1024UL * 1024UL * 1024UL;
        }

        private void TrimIdleWorkers()
        {
            if (_activeRunCts != null || _disposed) return;

            var idleFor = DateTime.UtcNow - _lastWorkCompletedUtc;
            lock (_workersLock)
            {
                _workers.RemoveAll(worker => worker.HasExited);
                int keep = 1;
                if (idleFor < TimeSpan.FromMinutes(3))
                    keep = Math.Max(keep, _workers.Count);

                while (_workers.Count > keep)
                {
                    var worker = _workers[_workers.Count - 1];
                    _workers.RemoveAt(_workers.Count - 1);
                    worker.Shutdown();
                }
            }
        }

        private void RaiseProgress(AnalysisProgressEventArgs args)
        {
            ProgressChanged?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _idleTimer.Dispose();
            ShutdownAllWorkers();
        }

        private void ShutdownAllWorkers()
        {
            _activeClassifier?.Dispose();
            _activeClassifier = null;
            lock (_workersLock)
            {
                foreach (var worker in _workers)
                    worker.Shutdown(forceKill: true);
                _workers.Clear();
            }
        }

        private void ShutdownExtraWorkers(int keepCount)
        {
            lock (_workersLock)
            {
                _workers.RemoveAll(worker => worker.HasExited);
                while (_workers.Count > Math.Max(1, keepCount))
                {
                    var worker = _workers[_workers.Count - 1];
                    _workers.RemoveAt(_workers.Count - 1);
                    worker.Shutdown(forceKill: true);
                }
            }
        }

        private class PythonWorker
        {
            private readonly int _id;
            private readonly IProjectPathResolver _pathResolver;
            private readonly ILogger _logger;
            private readonly Action<AnalysisProgressEventArgs> _raiseProgress;
            private readonly object _writeLock = new object();
            private TaskCompletionSource<bool> _readyTcs = new TaskCompletionSource<bool>();
            private TaskCompletionSource<PythonVideoResult> _activeTcs;
            private string _activeRequestId;
            private Process _process;
            public int ProcessID => _process?.Id ?? -1;

            public bool IsBusy => _activeTcs != null;
            public bool HasExited => _process == null || _process.HasExited;

            public PythonWorker(int id, IProjectPathResolver pathResolver, ILogger logger, Action<AnalysisProgressEventArgs> raiseProgress)
            {
                _id = id;
                _pathResolver = pathResolver;
                _logger = logger;
                _raiseProgress = raiseProgress;
            }

            public void Start()
            {
                _readyTcs = new TaskCompletionSource<bool>();
                string pythonPath = Path.Combine(_pathResolver.ResolveProjectRoot(), "venv", "Scripts", "python.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    WorkingDirectory = _pathResolver.ResolveProjectRoot(),
                    Arguments = $"-u \"{_pathResolver.ResolveYoloScriptPath()}\" --worker-json",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _process = Process.Start(psi);
                _ = Task.Run(ReadStdoutLoop);
                _ = Task.Run(ReadStderrLoop);
            }

            public Task WaitUntilReadyAsync(CancellationToken token)
            {
                return _readyTcs.Task.WaitAsync(token);
            }

            public async Task<PythonVideoResult> AnalyzeVideoAsync(string videoPath, AnalysisBatchContext context, CancellationToken token)
            {
                await WaitUntilReadyAsync(token);
                _activeRequestId = Guid.NewGuid().ToString("N");
                _activeTcs = new TaskCompletionSource<PythonVideoResult>();
                System.Diagnostics.Debug.Print("ANALYZING: " + ProcessID.ToString());

                Send(new
                {
                    command = "analyze_video",
                    request_id = _activeRequestId,
                    video_path = videoPath,
                    context = new
                    {
                        run_name = context.RunName,
                        run_folder = context.RunFolder,
                        location = context.Location,
                        upstream_direction = context.UpstreamDirection,
                        fast_mode = context.FastMode,
                        pending_image_folder = context.PendingImageFolder
                    }
                });

                try
                {
                    return await _activeTcs.Task.WaitAsync(token);
                }
                finally
                {
                    _activeTcs = null;
                    _activeRequestId = null;
                }
            }

            public void Shutdown(bool forceKill = false)
            {
                try
                {
                    if (_process != null && !_process.HasExited)
                    {
                        Send(new { command = "shutdown", request_id = Guid.NewGuid().ToString("N") });
                        if (!_process.WaitForExit(2000) || forceKill)
                            KillProcessTree(_process);
                    }
                }
                catch { }
            }

            public void Kill(bool restart = true)
            {
                try
                {
                    if (_process != null && !_process.HasExited)
                    {
                        KillProcessTree(_process);
                        _process.WaitForExit(2000);
                    }
                }
                catch { }
                finally
                {
                    _activeTcs?.TrySetCanceled();
                    if (restart)
                        Start();
                }
            }

            private static void KillProcessTree(Process process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    try { process.Kill(); } catch { }
                }
            }

            private void Send(object command)
            {
                string json = JsonSerializer.Serialize(command);
                lock (_writeLock)
                {
                    _process.StandardInput.WriteLine(json);
                    _process.StandardInput.Flush();
                }
            }

            private void ReadStdoutLoop()
            {
                try
                {
                    string line;
                    while ((line = _process.StandardOutput.ReadLine()) != null)
                    {
                        System.Diagnostics.Debug.Print(line);

                        if (line.StartsWith("[PROGRESS] FRAME:", StringComparison.Ordinal))
                        {
                            _raiseProgress(new AnalysisProgressEventArgs
                            {
                                EventType = "frame_progress",
                                FrameInfo = FormatFrameProgress(line.Substring("[PROGRESS] FRAME:".Length))
                            });
                            continue;
                        }

                        if (!line.StartsWith("{", StringComparison.Ordinal))
                            continue;

                        using var document = JsonDocument.Parse(line);
                        HandleEvent(document.RootElement);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Python worker {WorkerId} stdout loop failed", _id);
                    _readyTcs.TrySetException(ex);
                    _activeTcs?.TrySetException(ex);
                }
            }

            private void ReadStderrLoop()
            {
                try
                {
                    string line;
                    while ((line = _process.StandardError.ReadLine()) != null)
                        _logger?.LogWarning("Python worker {WorkerId}: {Line}", _id, line);
                }
                catch { }
            }

            private void HandleEvent(JsonElement root)
            {
                string eventName = GetString(root, "event");
                if (eventName == "ready")
                {
                    _readyTcs.TrySetResult(true);
                    return;
                }

                if (eventName == "video_started")
                {
                    _raiseProgress(new AnalysisProgressEventArgs
                    {
                        EventType = "video_started",
                        FileName = GetString(root, "filename")
                    });
                    return;
                }

                if (eventName == "video_finished")
                {
                    _activeTcs?.TrySetResult(PythonVideoResult.FromJson(root));
                    return;
                }

                if (eventName == "video_failed" || eventName == "error")
                {
                    _activeTcs?.TrySetException(new Exception(GetString(root, "error") ?? GetString(root, "message") ?? "Python worker failed."));
                    return;
                }
            }

            private static string FormatFrameProgress(string payload)
            {
                var parts = payload.Split('/');
                if (parts.Length != 2)
                    return "Video progress: ?";
                if (parts[1] == "?")
                    return $"Frame {parts[0]}";
                return int.TryParse(parts[0], out int current) &&
                       int.TryParse(parts[1], out int total) &&
                       total > 0
                    ? $"Video progress: {current * 100 / total}%"
                    : "Video progress: ?";
            }
        }

        private sealed class PythonClassifierProcess : IDisposable
        {
            private readonly IProjectPathResolver _pathResolver;
            private readonly ILogger _logger;
            private readonly object _writeLock = new object();
            private readonly TaskCompletionSource<bool> _readyTcs = new TaskCompletionSource<bool>();
            private TaskCompletionSource<Dictionary<string, ClassificationResult>> _activeTcs;
            private Process _process;

            public PythonClassifierProcess(IProjectPathResolver pathResolver, ILogger logger)
            {
                _pathResolver = pathResolver;
                _logger = logger;
            }

            public async Task StartAsync(CancellationToken token)
            {
                string pythonPath = Path.Combine(_pathResolver.ResolveProjectRoot(), "venv", "Scripts", "python.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    WorkingDirectory = _pathResolver.ResolveProjectRoot(),
                    Arguments = $"-u \"{_pathResolver.ResolveYoloScriptPath()}\" --classifier-json",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _process = Process.Start(psi);
                _ = Task.Run(ReadStdoutLoop);
                _ = Task.Run(ReadStderrLoop);
                await _readyTcs.Task.WaitAsync(token);
            }

            public async Task<Dictionary<string, ClassificationResult>> ClassifyBatchAsync(
                List<string> imagePaths,
                string classifiedFolder,
                CancellationToken token)
            {
                _activeTcs = new TaskCompletionSource<Dictionary<string, ClassificationResult>>();
                Send(new
                {
                    command = "classify_batch",
                    request_id = Guid.NewGuid().ToString("N"),
                    image_paths = imagePaths,
                    classified_folder = classifiedFolder
                });

                try
                {
                    return await _activeTcs.Task.WaitAsync(token);
                }
                finally
                {
                    _activeTcs = null;
                }
            }

            private void Send(object command)
            {
                string json = JsonSerializer.Serialize(command);
                lock (_writeLock)
                {
                    _process.StandardInput.WriteLine(json);
                    _process.StandardInput.Flush();
                }
            }

            private void ReadStdoutLoop()
            {
                try
                {
                    string line;
                    while ((line = _process.StandardOutput.ReadLine()) != null)
                    {
                        if (!line.StartsWith("{", StringComparison.Ordinal))
                            continue;

                        using var document = JsonDocument.Parse(line);
                        string eventName = GetString(document.RootElement, "event");
                        if (eventName == "ready")
                        {
                            _readyTcs.TrySetResult(true);
                            continue;
                        }

                        if (eventName == "classification_finished")
                        {
                            _activeTcs?.TrySetResult(ReadClassifications(document.RootElement));
                            continue;
                        }

                        if (eventName == "error")
                            _activeTcs?.TrySetException(new Exception(GetString(document.RootElement, "message") ?? "Classifier failed."));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Python classifier stdout loop failed");
                    _readyTcs.TrySetException(ex);
                    _activeTcs?.TrySetException(ex);
                }
            }

            private void ReadStderrLoop()
            {
                try
                {
                    string line;
                    while ((line = _process.StandardError.ReadLine()) != null)
                        _logger?.LogWarning("Python classifier: {Line}", line);
                }
                catch { }
            }

            private static Dictionary<string, ClassificationResult> ReadClassifications(JsonElement root)
            {
                var results = new Dictionary<string, ClassificationResult>(StringComparer.OrdinalIgnoreCase);
                if (!root.TryGetProperty("results", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
                    return results;

                foreach (var item in items.EnumerateArray())
                {
                    string originalPath = GetString(item, "original_path");
                    if (string.IsNullOrWhiteSpace(originalPath))
                        continue;

                    results[originalPath] = new ClassificationResult
                    {
                        Species = GetString(item, "species") ?? "No species",
                        SpeciesConfidence = GetString(item, "species_confidence") ?? "0.0000",
                        FinalPath = GetString(item, "final_path") ?? originalPath
                    };
                }

                return results;
            }

            public void Dispose()
            {
                try
                {
                    if (_process != null && !_process.HasExited)
                    {
                        Send(new { command = "shutdown", request_id = Guid.NewGuid().ToString("N") });
                        if (!_process.WaitForExit(2000))
                            _process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    try { _process?.Kill(entireProcessTree: true); } catch { }
                }
            }
        }

        private class ClassificationResult
        {
            public string Species { get; set; } = "No species";
            public string SpeciesConfidence { get; set; } = "0.0000";
            public string FinalPath { get; set; } = string.Empty;
        }

        private class PythonVideoResult
        {
            public List<Dictionary<string, string>> Tracks { get; } = new List<Dictionary<string, string>>();
            public Dictionary<string, string> NoFishRow { get; } = new Dictionary<string, string>();

            public static PythonVideoResult FromJson(JsonElement root)
            {
                var result = new PythonVideoResult();
                if (root.TryGetProperty("tracks", out JsonElement tracks) && tracks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var track in tracks.EnumerateArray())
                        result.Tracks.Add(ToDictionary(track));
                }

                if (root.TryGetProperty("no_fish_row", out JsonElement noFish) && noFish.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in ToDictionary(noFish))
                        result.NoFishRow[pair.Key] = pair.Value;
                }

                return result;
            }
        }

        private static Dictionary<string, string> ToDictionary(JsonElement element)
        {
            var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
                dictionary[property.Name] = JsonValueToString(property.Value);
            return dictionary;
        }

        private static string JsonValueToString(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
        }

        private static string GetString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }
}
