# ****************************************************************
# File: main.py
# Description: Main video processing script for YOLO and DeepSort.
# Author: Aden
# Contributers: Aleks, Reid
# Notes: N/A
# ****************************************************************

import sys
print("[PROGRESS] STARTUP", flush=True)

import csv
import importlib
import os
import shutil
import subprocess
import tempfile
import warnings
from collections import Counter
from contextlib import contextmanager
from dataclasses import dataclass, field
from typing import List

import cv2
import numpy as np

from extract_timestamp import extractTimestamFromFrame, check_tesseract, probe_video_timestamp
from tracking.deepsort_tracker import DeepSortTracker

# ========================================================================
# STARTUP CONFIGURATION
# ========================================================================

# Keep OpenCV logging quieter (FFmpeg decode warnings can be very noisy on damaged ASF streams).
try:
    cv2.utils.logging.setLogLevel(cv2.utils.logging.LOG_LEVEL_ERROR)
except Exception:
    try:
        cv2.setLogLevel(cv2.LOG_LEVEL_ERROR)
    except Exception:
        pass

# Suppress deprecation warning from pkg_resources
warnings.filterwarnings(
    "ignore",
    category=UserWarning,
    message="pkg_resources is deprecated"
)


def _load_yolo_model(model_path):
    """Load the YOLO detector lazily so startup can fail gracefully."""
    try:
        ultralytics = importlib.import_module("ultralytics")
        return ultralytics.YOLO(model_path)
    except Exception as e:
        print(f"WARNING: Could not load YOLO model '{model_path}': {e}")
        return None


def _load_classifier_model(model_path):
    """Load the fish species classifier from whichever Keras package is available."""
    loader_attempts = [
        ("tensorflow.keras.models", {"compile": False}),
        ("keras.models", {"compile": False}),
        ("tensorflow.keras.models", {}),
        ("keras.models", {}),
    ]

    last_error = None
    for module_name, load_kwargs in loader_attempts:
        try:
            keras_models = importlib.import_module(module_name)
            return keras_models.load_model(model_path, **load_kwargs)
        except Exception as e:
            last_error = e

    print(f"WARNING: Could not load classifier model '{model_path}': {last_error}")
    return None


def _load_keras_image_utils():
    """Resolve Keras image helpers from tensorflow.keras or standalone keras."""
    for module_name in ("tensorflow.keras.preprocessing.image", "keras.preprocessing.image"):
        try:
            keras_image = importlib.import_module(module_name)
            return keras_image.load_img, keras_image.img_to_array
        except Exception:
            continue
    return None, None


def _get_classifier_input_size(model, default=(150, 150)):
    """Infer classifier input dimensions from the loaded model when possible."""
    try:
        shape = getattr(model, "input_shape", None)
        if shape and len(shape) >= 3 and shape[1] and shape[2]:
            return int(shape[1]), int(shape[2])
    except Exception:
        pass
    return default


def _get_classifier_preprocess_mode(model):
    """Return the expected caller-side preprocessing mode for the loaded classifier."""
    try:
        layer_names = [str(getattr(layer, "name", "")).lower() for layer in getattr(model, "layers", [])]
        has_mobilenet_backbone = any("mobilenet" in name for name in layer_names)
        has_internal_rescaling = any(name.startswith("rescaling") for name in layer_names)
        if has_internal_rescaling:
            return "raw_255"
        if has_mobilenet_backbone:
            return "mobilenet_v2"
    except Exception:
        pass
    return "zero_one"


def _resolve_classifier_model_path():
    """Prefer the native .keras classifier artifact inside the models folder."""
    candidates = [
        os.path.join(PROJECT_ROOT, "models", "fish_classifier_model.keras"),
        os.path.join(PROJECT_ROOT, "models", "fish_classifier_model.h5"),
    ]
    for path in candidates:
        if os.path.exists(path):
            return path
    # Keep old default if none found so warning message still shows attempted path.
    return candidates[0]

# ========================================================================
# PATHS AND RUNTIME CONSTANTS
# ========================================================================

PROJECT_ROOT = os.path.dirname(os.path.abspath(__file__))


# CSV export schema
CSV_KEYS = [
    "video_file",         # col 0
    "location",           # col 1
    "species",            # col 2
    "species_confidence", # col 3
    "likely_class",       # col 4
    "confidence",         # col 5
    "direction",          # col 6
    "start_time_sec",     # col 7
    "end_time_sec",       # col 8
    "video_timestamp",    # col 9
    "run",                # col 10
]
NO_FISH_CSV_KEYS = ["video_file", "location", "video_timestamp"]
TESSERACT_AVAILABLE = check_tesseract()

# Detector configuration
MODEL = _load_yolo_model("models/fish_detector4.pt")
STRICT_YOLO_CONFIDENCE_THRESHOLD = float(os.getenv("FISHLENS_YOLO_CONFIDENCE_THRESHOLD", "0.42"))
LOOSE_YOLO_CONFIDENCE_THRESHOLD = float(os.getenv("FISHLENS_LOOSE_YOLO_CONFIDENCE_THRESHOLD", "0.34"))
ENABLE_LOOSE_RETRY = os.getenv("FISHLENS_ENABLE_LOOSE_RETRY", "1") == "1"
YOLO_CONFIDENCE_THRESHOLD = STRICT_YOLO_CONFIDENCE_THRESHOLD  # Adjustable: lower = detects more fish (but more false positives), higher = more selective
MIN_DETECTION_BOX_AREA = max(50, int(os.getenv("FISHLENS_MIN_DETECTION_BOX_AREA", "300")))
NO_FISH = os.path.join(PROJECT_ROOT, "no_fish")

# Corner-artifact filter: fixed IR illuminators or lens rings in camera corners produce
# small, high-confidence detections that YOLO mistakes for fish.  A detection is rejected
# if its bounding box is small (< CORNER_ARTIFACT_MAX_SIZE fraction of frame in both
# dimensions) AND its center falls within CORNER_ARTIFACT_ZONE of both a horizontal
# and a vertical frame edge (i.e. inside one of the four corner zones).
CORNER_ARTIFACT_ZONE     = 0.15   # 15 % from each edge defines the corner zone
CORNER_ARTIFACT_MAX_SIZE = 0.20   # box must be < 20 % of frame width AND height

# Tracking/export configuration
FPS_DEFAULT = 30 
MAX_EXPORT_PER_VIDEO = 5

CLI_INPUT_PATH = sys.argv[1].strip() if len(sys.argv) > 1 else ""


def _resolve_run_folder():
    env_run_folder = os.getenv("FISHLENS_RUN_FOLDER", "").strip()
    if env_run_folder:
        return env_run_folder

    if CLI_INPUT_PATH:
        return os.path.join(PROJECT_ROOT, "results", "cli")

    return ""


# Output files are determined by the active run folder passed via FISHLENS_RUN_FOLDER.
_RUN_FOLDER = _resolve_run_folder()
_IS_DEBUG_RUN = _RUN_FOLDER and os.path.basename(_RUN_FOLDER).lower() == "debug"
OUTPUT_CSV = os.path.join(PROJECT_ROOT, "fish_summary.csv")
FISH_IMAGE_DIR = os.path.join(PROJECT_ROOT, "fish_images")


if not _RUN_FOLDER:
    print("[ERROR] FISHLENS_RUN_FOLDER is not set. A run must be active before starting analysis.", flush=True)

os.makedirs(_RUN_FOLDER, exist_ok=True) if _RUN_FOLDER else None
_ALL_HISTORY_DIR = os.path.dirname(_RUN_FOLDER) if _RUN_FOLDER else PROJECT_ROOT
os.makedirs(_ALL_HISTORY_DIR, exist_ok=True)
_RUN_NAME = os.path.basename(_RUN_FOLDER) if _RUN_FOLDER else ""

if _IS_DEBUG_RUN:
    # Debug mode: single CSV, never writes to all_history or session files
    OUTPUT_CSV          = os.path.join(_RUN_FOLDER, "debug.csv")
    SESSION_CSV         = None
    SESSION_NO_FISH_CSV = None
    MASTER_FISH_CSV     = None
else:
    # Normal run: session files (wiped on startup) + persistent masters
    SESSION_CSV         = os.path.join(_RUN_FOLDER, "session_fish.csv")      if _RUN_FOLDER else None
    SESSION_NO_FISH_CSV = os.path.join(_RUN_FOLDER, "session_no_fish.csv")   if _RUN_FOLDER else None
    OUTPUT_CSV          = os.path.join(_RUN_FOLDER, "run_master.csv")        if _RUN_FOLDER else None
    MASTER_FISH_CSV     = os.path.join(_ALL_HISTORY_DIR, "all_history.csv")  if _RUN_FOLDER else None

FISH_IMAGE_DIR = os.path.join(PROJECT_ROOT, "fish_images")

# Runtime tuning (primarily fed by the WPF app through environment variables)
FISHLENS_LOCATION = os.getenv("FISHLENS_LOCATION", "Unknown").strip() or "Unknown"
FAST_MODE = os.getenv("FISHLENS_FAST_MODE", "1") == "1"
STRICT_FRAME_STRIDE = max(1, int(os.getenv("FISHLENS_STRICT_FRAME_STRIDE", "3")))
FRAME_STRIDE = STRICT_FRAME_STRIDE

YOLO_IMGSZ = max(320, int(os.getenv("FISHLENS_YOLO_IMGSZ", "448" if FAST_MODE else "512")))
SAVE_TIMESTAMP_DEBUG_FRAMES = os.getenv("FISHLENS_SAVE_TIMESTAMP_DEBUG", "0") == "1"
TIMESTAMP_MAX_ATTEMPTS = max(1, int(os.getenv("FISHLENS_TIMESTAMP_MAX_ATTEMPTS", "4" if FAST_MODE else "8")))
SUPPRESS_CODEC_WARNINGS = os.getenv("FISHLENS_SUPPRESS_CODEC_WARNINGS", "1") == "1"
VIDEO_TIMESTAMP_PROBE_FRAMES = max(1, int(os.getenv("FISHLENS_VIDEO_TS_PROBE_FRAMES", "6" if FAST_MODE else "12")))
STRICT_MIN_TRACK_DURATION_SEC = max(0.1, float(os.getenv("FISHLENS_MIN_TRACK_DURATION_SEC", "0.65")))
LOOSE_MIN_TRACK_DURATION_SEC = max(0.1, float(os.getenv("FISHLENS_LOOSE_MIN_TRACK_DURATION_SEC", "0.45")))
MIN_TRACK_DURATION_SEC = STRICT_MIN_TRACK_DURATION_SEC
MIN_TRACK_TRAVEL_PX = max(0.0, float(os.getenv("FISHLENS_MIN_TRACK_TRAVEL_PX", "8")))

# Classifier configuration
CLASSIFIER_MODEL_PATH = _resolve_classifier_model_path()
CLASSIFIER_MODEL = _load_classifier_model(CLASSIFIER_MODEL_PATH)
LOAD_IMG, IMG_TO_ARRAY = _load_keras_image_utils()
CLASS_NAMES = ["Chinook", "Omykiss"]
IMAGE_SIZE = _get_classifier_input_size(CLASSIFIER_MODEL, default=(150, 150))
CLASSIFIER_PREPROCESS_MODE = _get_classifier_preprocess_mode(CLASSIFIER_MODEL)

# Create and initialize folders
os.makedirs(NO_FISH, exist_ok=True)
os.makedirs(FISH_IMAGE_DIR, exist_ok=True)

# ========================================================================
# CSV INITIALIZATION
# ========================================================================

# Session CSVs are wiped on startup; master CSVs are only created if missing/empty.
def _initialize_csv_header(path, keys, overwrite=False):
    if not path:
        return

    try:
        should_write = overwrite or not os.path.exists(path) or os.path.getsize(path) == 0
        if not should_write:
            return

        mode = "w" if overwrite else "a"
        with open(path, mode, newline="") as f:
            csv.DictWriter(f, fieldnames=keys).writeheader()
    except Exception as ex:
        print(f"[WARNING] Could not initialize CSV header for {path}: {ex}")

def _init_csvs():
    for path, keys in [(SESSION_CSV, CSV_KEYS), (SESSION_NO_FISH_CSV, NO_FISH_CSV_KEYS)]:
        _initialize_csv_header(path, keys, overwrite=True)

    if _IS_DEBUG_RUN:
        _initialize_csv_header(OUTPUT_CSV, CSV_KEYS, overwrite=False)
    else:
        for path, keys in [(OUTPUT_CSV, CSV_KEYS), (MASTER_FISH_CSV, CSV_KEYS)]:
            _initialize_csv_header(path, keys, overwrite=False)

_init_csvs()

# Signal to the host application that models are loaded and we are ready for work.
print("[PROGRESS] READY", flush=True)

# ========================================================================
# LIGHTWEIGHT RUNTIME DATA CONTAINERS
# ========================================================================

@dataclass
class FrameData:
    """Per-frame state shared between YOLO, DeepSort, and finalization logic."""
    f_index: int = 0
    f_found_fish: bool = False
    f_detections: List = field(default_factory=list)
    f_pos_ms: float = 0.0  # Actual decoder position in milliseconds (from CAP_PROP_POS_MSEC)

class VideoData:
    """Mutable per-video state accumulated across the analysis pipeline."""
    def __init__(self):
        self.v_filename = "default.mp4"
        self.v_frames_with_fish = 0
        self.v_frames_without_fish = 0
        self.v_total_frames = 0
        self.v_frame_index = 0
        self.v_fps = FPS_DEFAULT
        self.v_avg_confidence_YL = 0.0
        self.v_most_common_class = "unknown"
        self.v_found_fish = False
        self.v_active_tracks = {}
        self.v_finished_tracks = []
        self.v_current_track_ids = set()
        self.v_confidence_sum = 0.0
        self.v_confidence_count = 0 
        self.v_video_timestamp = None
        self.frame_width = 640


# ========================================================================
# VIDEO I/O AND METADATA HELPERS
# ========================================================================

@contextmanager
def _suppress_stderr(enabled=True):
    """Temporarily silence noisy native stderr output from OpenCV/FFmpeg wrappers."""
    if not enabled:
        yield
        return

    try:
        stderr_fd = sys.stderr.fileno()
        saved_stderr_fd = os.dup(stderr_fd)
        devnull_fd = os.open(os.devnull, os.O_WRONLY)
        os.dup2(devnull_fd, stderr_fd)
        try:
            yield
        finally:
            os.dup2(saved_stderr_fd, stderr_fd)
            os.close(saved_stderr_fd)
            os.close(devnull_fd)
    except Exception:
        # If redirection is not supported in this runtime, continue normally.
        yield


def _video_capture_open(video_path):
    """Open a video capture while optionally suppressing codec warning spam."""
    with _suppress_stderr(SUPPRESS_CODEC_WARNINGS):
        return cv2.VideoCapture(video_path)


def _video_capture_read(cap):
    """Single indirection point for cap.read(), useful for timestamp probing hooks."""
    return cap.read()


def _resolve_ffprobe_path():
    """Locate ffprobe from env, PATH, or an ffmpeg sibling directory."""
    env_path = os.getenv("FISHLENS_FFPROBE_PATH", "").strip()
    if env_path and os.path.isfile(env_path):
        return env_path

    ffprobe_path = shutil.which("ffprobe")
    if ffprobe_path:
        return ffprobe_path

    ffmpeg_path = shutil.which("ffmpeg")
    if ffmpeg_path:
        sibling = os.path.join(os.path.dirname(ffmpeg_path), "ffprobe.exe")
        if os.path.isfile(sibling):
            return sibling

    return None


def _probe_video_duration_sec(video_path):
    """Read container duration via ffprobe for export-only display labels."""
    ffprobe_path = _resolve_ffprobe_path()
    if not ffprobe_path or not os.path.isfile(video_path):
        return None

    cmd = [
        ffprobe_path,
        "-v", "error",
        "-show_entries", "format=duration",
        "-of", "default=noprint_wrappers=1:nokey=1",
        video_path
    ]

    try:
        result = subprocess.run(cmd, capture_output=True, text=True, check=False)
        if result.returncode != 0:
            return None

        value = (result.stdout or "").strip()
        duration = float(value)
        return duration if duration > 0 else None
    except Exception:
        return None


def _format_mmss(seconds):
    """Format a floating-point second count as M:SS."""
    seconds = max(0.0, float(seconds or 0.0))
    total = int(round(seconds))
    minutes = total // 60
    secs = total % 60
    return f"{minutes}:{secs:02d}"


def _enrich_tracks_with_duration(video_tracks, processed_video_path, source_video_path):
    """Second pass: use ffprobe durations to compute export-only duration labels.
    Does not alter detection/tracking logic or existing exported fields.
    """
    if not video_tracks:
        return

    processed_duration = _probe_video_duration_sec(processed_video_path)
    source_duration = _probe_video_duration_sec(source_video_path) if source_video_path else None

    duration_scale = 1.0
    if processed_duration and source_duration and processed_duration > 0:
        duration_scale = source_duration / processed_duration

    for track in video_tracks:
        try:
            start_sec = float(track.get("start_time_sec", 0.0))
            end_sec = float(track.get("end_time_sec", start_sec))
        except Exception:
            start_sec = 0.0
            end_sec = 0.0

        scaled_start = start_sec * duration_scale
        scaled_end = max(scaled_start, end_sec * duration_scale)
        track["duration"] = f"{_format_mmss(scaled_start)}-{_format_mmss(scaled_end)}"


# ========================================================================
# IMAGE, CSV, AND CONVERSION HELPERS
# ========================================================================

def enhance_image(crop):
    """Upscale and sharpen a crop before classifier inference and image export."""
    if crop is None or crop.size == 0:
        return None
    
    h, w = crop.shape[:2]
    
    # Upscale if crop is small
    if w < 200 or h < 200:
        scale = max(200 / w, 200 / h)
        new_w = int(w * scale)
        new_h = int(h * scale)
        crop = cv2.resize(crop, (new_w, new_h), interpolation=cv2.INTER_LANCZOS4)
    
    # Unsharp mask for clarity
    gaussian = cv2.GaussianBlur(crop, (0, 0), 1.5)
    crop = cv2.addWeighted(crop, 1.8, gaussian, -0.8, 0)
    
    # Clip values to valid range
    crop = np.clip(crop, 0, 255).astype(np.uint8)
    
    return crop

def _ensure_csv_schema(path, keys, fill_values=None):
    """Expand older CSVs to the current schema without dropping existing data."""
    if path is None or not os.path.exists(path) or os.path.getsize(path) == 0:
        return

    fill_values = fill_values or {}
    try:
        with open(path, newline="") as f:
            rows = list(csv.reader(f))
    except Exception:
        return

    if not rows:
        return

    header = rows[0]
    if header == keys:
        return

    expanded_rows = [keys]
    for row in rows[1:]:
        row_map = {}
        for idx, column_name in enumerate(header):
            row_map[column_name] = row[idx] if idx < len(row) else ""
        expanded_rows.append([row_map.get(key, fill_values.get(key, "")) for key in keys])

    with open(path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerows(expanded_rows)

def _flush_tracks_to_csv(tracks):
    """Append one video's fish tracks to the active session/master CSV outputs."""
    def _append_csv(path, rows, keys):
        if path is None:
            return
        fill_values = {"run": _RUN_NAME} if path in (OUTPUT_CSV, SESSION_CSV) else {}
        _ensure_csv_schema(path, keys, fill_values)
        needs_header = not os.path.exists(path) or os.path.getsize(path) == 0
        with open(path, "a", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=keys, extrasaction="ignore")
            if needs_header:
                writer.writeheader()
            writer.writerows(rows)

    if _IS_DEBUG_RUN:
        _append_csv(OUTPUT_CSV, tracks, CSV_KEYS)
    else:
        _append_csv(SESSION_CSV, tracks, CSV_KEYS)
        _append_csv(OUTPUT_CSV, tracks, CSV_KEYS)
        _append_csv(MASTER_FISH_CSV, tracks, CSV_KEYS)

def convert_asf_to_mp4(video_path):
    """Convert ASF/WMV inputs to a temporary MP4 when that improves decode reliability."""
    ext = os.path.splitext(video_path)[1].lower()
    if ext not in ('.asf', '.wmv'):
        return video_path, False

    fd, output_path = tempfile.mkstemp(suffix='.mp4')
    os.close(fd)

    # Prefer ffmpeg CLI conversion when available to avoid noisy OpenCV decode warnings.
    ffmpeg_path = shutil.which("ffmpeg")
    if ffmpeg_path:
        ffmpeg_cmd = [
            ffmpeg_path,
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", video_path,
            "-an",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "23",
            output_path
        ]
        try:
            result = subprocess.run(ffmpeg_cmd, capture_output=True, text=True, check=False)
            if result.returncode == 0 and os.path.exists(output_path) and os.path.getsize(output_path) > 0:
                print(f"Converted ASF to MP4 (temp): {os.path.basename(video_path)}")
                return output_path, True
            else:
                print("Warning: ffmpeg conversion failed, falling back to OpenCV conversion.")
        except Exception:
            print("Warning: ffmpeg invocation failed, falling back to OpenCV conversion.")

    cap = _video_capture_open(video_path)
    if not cap.isOpened():
        print(f"Warning: Could not open ASF for conversion: {video_path}")
        _cleanup_temp(output_path)
        return video_path, False

    # ASF files frequently report incorrect frame rates via the Windows codec
    # (e.g. 1fps or 7.5fps when the camera ran at 30fps). Measure the actual
    # frame interval from the decoder's own POS_MSEC clock by sampling the
    # first PROBE_FRAMES frames, then derive fps from the real elapsed time.
    PROBE_FRAMES = 30
    probe_timestamps = []
    for _ in range(PROBE_FRAMES):
        ts_ms = cap.get(cv2.CAP_PROP_POS_MSEC)
        probe_timestamps.append(ts_ms)
        ret, _ = _video_capture_read(cap)
        if not ret:
            break
    cap.set(cv2.CAP_PROP_POS_FRAMES, 0)  # rewind for actual conversion

    fps = FPS_DEFAULT  # start with safe default
    if len(probe_timestamps) >= 2:
        intervals = [probe_timestamps[i+1] - probe_timestamps[i]
                     for i in range(len(probe_timestamps) - 1)
                     if probe_timestamps[i+1] > probe_timestamps[i]]
        if intervals:
            median_interval_ms = sorted(intervals)[len(intervals) // 2]
            if median_interval_ms > 0:
                measured_fps = 1000.0 / median_interval_ms
                if 5.0 <= measured_fps <= 120.0:
                    fps = measured_fps
    # Also cross-check against the metadata fps; if it's sane, prefer it.
    meta_fps = cap.get(cv2.CAP_PROP_FPS)
    if 5.0 <= meta_fps <= 120.0 and fps == FPS_DEFAULT:
        fps = meta_fps
    print(f"ASF OpenCV fallback: using fps={fps:.2f} for {os.path.basename(video_path)}")

    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))

    if width <= 0 or height <= 0:
        ret, first_frame = _video_capture_read(cap)
        if not ret or first_frame is None:
            cap.release()
            print(f"Warning: ASF conversion failed (no readable frames): {video_path}")
            _cleanup_temp(output_path)
            return video_path, False
        height, width = first_frame.shape[:2]
        cap.set(cv2.CAP_PROP_POS_FRAMES, 0)

    writer = cv2.VideoWriter(
        output_path,
        cv2.VideoWriter_fourcc(*'mp4v'),
        fps,
        (width, height)
    )

    if not writer.isOpened():
        cap.release()
        print(f"Warning: Could not create MP4 writer for: {video_path}")
        _cleanup_temp(output_path)
        return video_path, False

    frame_count = 0
    while True:
        ret, frame = _video_capture_read(cap)
        if not ret or frame is None:
            break
        writer.write(frame)
        frame_count += 1

    cap.release()
    writer.release()

    if frame_count == 0:
        print(f"Warning: ASF conversion produced zero frames: {video_path}")
        _cleanup_temp(output_path)
        return video_path, False

    print(f"Converted ASF to MP4 (temp): {os.path.basename(video_path)}")
    return output_path, True

def _cleanup_temp(path):
    """Best-effort cleanup for temporary converted videos."""
    try:
        if path and os.path.exists(path):
            os.remove(path)
    except OSError:
        pass

# ========================================================================
# CORE ANALYSIS PIPELINE
# ========================================================================

def analyze_yolo_detections(frame, model, frameData, vidData):
    """Run YOLO on one frame and keep only fish-like detections that survive filters."""

    # Run YOLO on frame
    results = model.predict(
        source=frame,
        verbose=False,
        stream=False,
        save=False,
        imgsz=YOLO_IMGSZ,
        conf=YOLO_CONFIDENCE_THRESHOLD
    )

    frame_h_px, frame_w_px = frame.shape[:2]

    # Begin YOLO post-analysis
    if results:
        r = results[0]
        detection_count = 0
        for box in r.boxes:
            x1, y1, x2, y2 = map(int, box.xyxy[0].cpu().numpy())
            conf_arr = box.conf.cpu().numpy()
            conf = float(conf_arr[0]) if conf_arr.size > 0 and conf_arr[0] is not None else 0.0
            cls_arr = box.cls.cpu().numpy()
            cls_id = int(cls_arr[0]) if cls_arr.size > 0 and cls_arr[0] is not None else -1
            box_area = (x2 - x1) * (y2 - y1)
            
            if box_area < MIN_DETECTION_BOX_AREA or conf < YOLO_CONFIDENCE_THRESHOLD:
                continue

            # Mark if YOLO detected a fish in frame.
            try:
                cls_name = model.names[cls_id].lower()
            except Exception:
                cls_name = str(cls_id)

            # Keep only fish-like classes to avoid tracking non-fish detections.
            fish_keywords = ("fish", "trout", "salmon", "chinook", "omykiss")
            if not any(keyword in cls_name for keyword in fish_keywords):
                continue

            frameData.f_found_fish = True
            detection_count += 1
            frameData.f_detections.append([x1, y1, x2, y2, conf, cls_id])
        

    
    # Increment video-level stats based on frame-level results
    vidData.v_found_fish = vidData.v_found_fish or frameData.f_found_fish
        
    vidData.v_frames_with_fish += 1 if frameData.f_found_fish else 0
    vidData.v_frames_without_fish += 0 if frameData.f_found_fish else 1

def process_yolo_results(frameData, vidData, model):
    """Update running video-level class/confidence stats from the current frame."""
    if frameData.f_detections:
        ids = [d[5] for d in frameData.f_detections]
        id_counter = Counter(ids)
        most_common_id = id_counter.most_common(1)[0][0]
        vidData.v_most_common_class = model.names[most_common_id]

        
        confidences = [d[4] for d in frameData.f_detections if d[5] == most_common_id]
        vidData.v_confidence_sum += sum(confidences)
        vidData.v_confidence_count += len(confidences)
        
        # Calculate running average
        if vidData.v_confidence_count > 0:
            vidData.v_avg_confidence_YL = (vidData.v_confidence_sum / vidData.v_confidence_count) * 100

def _score_species_crop(frame_shape, bbox, conf, track_age_frames):
    """Score a candidate species screenshot, preferring centered, fully visible fish."""
    frame_h, frame_w = frame_shape[:2]
    x1, y1, x2, y2 = bbox
    box_w = max(1.0, float(x2 - x1))
    box_h = max(1.0, float(y2 - y1))

    cx = (float(x1) + float(x2)) / 2.0
    cy = (float(y1) + float(y2)) / 2.0
    frame_cx = frame_w / 2.0
    frame_cy = frame_h / 2.0

    # Favor fish whose center is nearer the middle of the frame.
    dx = abs(cx - frame_cx) / max(1.0, frame_cx)
    dy = abs(cy - frame_cy) / max(1.0, frame_cy)
    center_distance = min(1.0, np.hypot(dx, dy) / np.hypot(1.0, 1.0))
    center_score = 1.0 - center_distance

    # Penalize fish that are leaving the frame or clipped against an edge.
    min_edge_distance = min(float(x1), float(y1), float(frame_w - x2), float(frame_h - y2))
    edge_clearance_target = max(12.0, min(frame_w, frame_h) * 0.08)
    edge_score = max(0.0, min(1.0, min_edge_distance / edge_clearance_target))

    # Prefer reasonably large, readable fish, but cap the benefit.
    area_fraction = (box_w * box_h) / max(1.0, float(frame_w * frame_h))
    area_score = min(1.0, np.sqrt(max(0.0, area_fraction) / 0.12))

    # Slightly prefer frames after the track has stabilized for a few detections.
    maturity_score = min(1.0, max(0, int(track_age_frames)) / 4.0)

    conf_score = max(0.0, min(1.0, float(conf)))
    return (
        conf_score * 0.35
        + center_score * 0.30
        + edge_score * 0.20
        + area_score * 0.10
        + maturity_score * 0.05
    )

def deepsort_analysis(tracker, frame, frameData, vidData):
    """Advance DeepSort tracks for the current frame and maintain per-track state."""

    # Use the tracker's default iou threshold (tuned in the tracker)
    frameData.f_detections = tracker.filterOverlaps(frameData.f_detections)

    tracked_objects = tracker.update(frameData.f_detections, frame)
    
    for obj in tracked_objects:
        trackId = obj["trackId"]
        vidData.v_current_track_ids.add(trackId)

        # Check if this is a newtrack
        is_new_track = trackId not in vidData.v_active_tracks

        if is_new_track:
            initial_conf = "LOW" if (vidData.v_video_timestamp and vidData.v_video_timestamp != "Not detected") else None
            initial_x = float(obj.get("centroid", (0, 0))[0])
            vidData.v_active_tracks[trackId] = {
                "start_frame": frameData.f_index,
                "confidences": [],
                "directions": [],
                "entry_x": initial_x,
                "last_x": initial_x,
                "best_conf": -1.0,
                "best_crop_score": -1.0,
                "best_crop": None,
                "video_timestamp": vidData.v_video_timestamp or "Not detected",
                "timestamp_confidence": initial_conf,
                "timestamp_attempts": 0
            }
            
            # Track created; timestamp OCR will be retried for several frames if needed.
            print(f"  New fish detected (Track {trackId}) at frame {frameData.f_index} - initial ts_conf={initial_conf}")

        # Retry timestamp OCR on early frames of each track until one succeeds.
        track_data = vidData.v_active_tracks[trackId]
        current_confidence = track_data.get("timestamp_confidence")
        current_ts = track_data.get("video_timestamp", "Not detected")

        # Keep trying to get direct OCR if we only have LOW confidence (from probe) or no timestamp at all.
        if current_confidence in (None, "LOW"):
            attempts = track_data.get("timestamp_attempts", 0)
            if attempts < TIMESTAMP_MAX_ATTEMPTS:
                result = extractTimestamFromFrame(frame, False)
                track_data["timestamp_attempts"] = attempts + 1

                if result and result[0]:
                    old_ts = current_ts
                    old_conf = current_confidence
                    track_data["video_timestamp"] = result[0]
                    track_data["timestamp_confidence"] = result[1]
                    print(f"    Track {trackId}: Updated ts from '{old_ts}' ({old_conf}) to '{result[0]}' ({result[1]})")
                elif track_data["timestamp_attempts"] == TIMESTAMP_MAX_ATTEMPTS and current_confidence is None:
                    print(f"    Could not extract timestamp after {TIMESTAMP_MAX_ATTEMPTS} attempts")
                    if SAVE_TIMESTAMP_DEBUG_FRAMES:
                        debug_frame_path = f"debug_full_frame_track_{trackId}.jpg"
                        cv2.imwrite(debug_frame_path, frame)
                        print(f"    Saved full frame to: {debug_frame_path}")

        # Update track data per-frame
        vidData.v_active_tracks[trackId]["confidences"].append(obj["confidence"])
        vidData.v_active_tracks[trackId]["directions"].append(obj["direction"])

        x1, y1, x2, y2 = obj["bbox"]
        exit_x = (x1 + x2) / 2  # Center x-position at current frame
        vidData.v_active_tracks[trackId]["last_x"] = exit_x
        
        # Add 30% margin around the bounding box for zoomed-out view
        h, w = frame.shape[:2]
        margin_x = int((x2 - x1) * 0.3)
        margin_y = int((y2 - y1) * 0.3)
            
        x1_zoom = max(0, x1 - margin_x)
        y1_zoom = max(0, y1 - margin_y)
        x2_zoom = min(w, x2 + margin_x)
        y2_zoom = min(h, y2 + margin_y)
            
        crop = frame[y1_zoom:y2_zoom, x1_zoom:x2_zoom]
        conf = obj["confidence"]

        if crop is not None and crop.size > 0:
            track_age_frames = len(vidData.v_active_tracks[trackId]["confidences"])
            crop_score = _score_species_crop(frame.shape, (x1, y1, x2, y2), conf, track_age_frames)
            previous_best_conf = vidData.v_active_tracks[trackId].get("best_conf", -1.0)
            best_crop_score = vidData.v_active_tracks[trackId].get("best_crop_score", -1.0)

            if conf > previous_best_conf:
                vidData.v_active_tracks[trackId]["best_conf"] = conf

            should_replace_crop = (
                crop_score > best_crop_score
                or (
                    abs(crop_score - best_crop_score) <= 1e-6
                    and conf > previous_best_conf
                )
            )

            if should_replace_crop:
                vidData.v_active_tracks[trackId]["best_crop_score"] = crop_score
                vidData.v_active_tracks[trackId]["best_crop"] = crop.copy()
                vidData.v_active_tracks[trackId]["best_frame"] = frame.copy()

def finalize_tracks(frameData, vidData, termination_reason): 
    """Convert ended active tracks into exportable summaries."""

    if termination_reason not in ("disappeared", "forced"):
        raise ValueError("Invalid termination reason. Must be 'disappeared' or 'forced'.")
    
    if termination_reason == "disappeared":         
        disappeared_ids = set(vidData.v_active_tracks.keys()) - vidData.v_current_track_ids 
        for tid in disappeared_ids:
            track_data = vidData.v_active_tracks.pop(tid)
            track_dict = build_track_summary(tid, track_data, frameData, vidData, None, frame_width=getattr(vidData, "frame_width", 640))
            if track_dict:
                vidData.v_finished_tracks.append(track_dict)

    elif termination_reason == "forced":
        for tid, track_data in vidData.v_active_tracks.items():
            track_dict = build_track_summary(tid, track_data, frameData, vidData, None, frame_width=getattr(vidData, "frame_width", 640))
            if track_dict:
                vidData.v_finished_tracks.append(track_dict)

def build_track_summary(trackId, track_data, frameData, vidData, image_path=None, frame_width=640):
    """Build one export row for a finished track after duration/direction filtering."""
    duration_sec = (frameData.f_index - track_data["start_frame"]) / vidData.v_fps
    confidences = [c for c in track_data["confidences"] if c is not None]

    # Calculate overall direction inputs early so duration gate can consider motion.
    entry_x = track_data.get("entry_x", 0)
    exit_x = track_data.get("last_x", 0)
    travel_px = abs(float(exit_x) - float(entry_x))

    # Get best confidence for track early so duration gate can be adaptive.
    best_conf = track_data.get("best_conf", 0.0)
    best_conf_norm = best_conf if best_conf <= 1.0 else (best_conf / 100.0)

    # Keep very confident short tracks (common when fish is visible only briefly).
    min_duration_required = MIN_TRACK_DURATION_SEC
    if best_conf_norm >= 0.90:
        min_duration_required = max(0.15, MIN_TRACK_DURATION_SEC * 0.4)

    # Allow short, high-confidence moving tracks (often true fish entering/exiting fast).
    if best_conf_norm >= 0.85 and len(confidences) >= 2 and travel_px >= 8.0:
        one_frame_sec = 1.0 / max(1.0, float(vidData.v_fps or FPS_DEFAULT))
        min_duration_required = min(min_duration_required, one_frame_sec)

    if duration_sec < min_duration_required:
        print(
            f"  [FILTER] Track {trackId} dropped: duration {duration_sec:.2f}s < {min_duration_required:.2f}s "
            f"(best_conf={best_conf_norm:.2f}, points={len(confidences)}, travel_px={travel_px:.1f})"
        )
        return None

    # Calculate DeepSort average confidence
    avg_conf_DS = sum(confidences) / len(confidences) if confidences else 0.0

    # Get best confidence for track
    best_conf_pct = best_conf * 100 if best_conf <= 1.0 else best_conf
    
    # Calculate overall direction based on entry and exit positions
    # Determine if entry and exit are on same side of frame
    # Left side: x < frame_width/2, Right side: x >= frame_width/2
    entry_side = "left" if entry_x < frame_width / 2 else "right"
    exit_side = "left" if exit_x < frame_width / 2 else "right"
    
    # Determine direction
    directions = track_data["directions"]
    upstream_count = directions.count("upstream") if directions else 0
    downstream_count = directions.count("downstream") if directions else 0
    directional_votes = upstream_count + downstream_count
    net_dx = float(exit_x) - float(entry_x)
    min_net_dx = max(6.0, float(frame_width) * 0.02)

    overall_direction = "indecisive"
    if entry_side != exit_side:
        # Fish crossed frame halves: use tracker vote first.
        if upstream_count > downstream_count:
            overall_direction = "upstream"
        elif downstream_count > upstream_count:
            overall_direction = "downstream"
        elif net_dx <= -min_net_dx:
            overall_direction = "upstream"
        elif net_dx >= min_net_dx:
            overall_direction = "downstream"
    else:
        # Same-side tracks can still have clear direction from net movement.
        if travel_px >= MIN_TRACK_TRAVEL_PX and net_dx <= -min_net_dx:
            overall_direction = "upstream"
        elif travel_px >= MIN_TRACK_TRAVEL_PX and net_dx >= min_net_dx:
            overall_direction = "downstream"
        elif directional_votes >= 3:
            vote_ratio = max(upstream_count, downstream_count) / max(1, directional_votes)
            if vote_ratio >= 0.70:
                overall_direction = "upstream" if upstream_count > downstream_count else "downstream"

    # Reject low-motion indecisive tracks (common glare/debris false positives).
    if overall_direction == "indecisive" and travel_px < MIN_TRACK_TRAVEL_PX:
        print(f"  [FILTER] Track {trackId} dropped: indecisive + travel_px {travel_px:.1f} < {MIN_TRACK_TRAVEL_PX:.1f}")
        return None
    
    # Handle timestamp with confidence flag
    video_timestamp = track_data.get("video_timestamp") or vidData.v_video_timestamp or "Not detected"
    timestamp_confidence = track_data.get("timestamp_confidence")

    # Add * only if timestamp is LOW confidence
    if timestamp_confidence and timestamp_confidence == "LOW":
        video_timestamp = f"{video_timestamp}*"
    
    return {
        "trackId": trackId,
        "likely_class": vidData.v_most_common_class,
        "confidence": f"{(best_conf_pct / 100):.4f}" if best_conf_pct > 0 else f"{(vidData.v_avg_confidence_YL / 100):.4f}",
        "avg_confidence": f"{(best_conf_pct / 100):.4f}" if best_conf_pct > 0 else f"{(avg_conf_DS / 100):.4f}",
        "start_time_sec": f"{track_data['start_frame'] / vidData.v_fps:.2f}",
        "end_time_sec": f"{frameData.f_index / vidData.v_fps:.2f}",
        "direction": overall_direction,
        "best_crop": track_data.get("best_crop"),
        "species": "No data",
        "species_confidence": "0.0000",
        "video_timestamp": video_timestamp,
        "timestamp_confidence": timestamp_confidence,
        "best_frame": vidData.v_active_tracks.get(trackId, {}).get("best_frame") if trackId in vidData.v_active_tracks else None,
        # Spatial info forwarded so dedupe_fragmented_tracks can guard against merging two simultaneous fish.
        "entry_x": track_data.get("entry_x"),
        "exit_x": track_data.get("last_x"),
        "frame_width": frame_width,
        "best_conf_raw": float(best_conf_pct),
    }

def dedupe_fragmented_tracks(finished_tracks):
    """Merge likely duplicate rows created when one fish is split across tracker IDs."""
    if not finished_tracks:
        return finished_tracks

    def _pct(track):
        value = track.get("avg_confidence") or track.get("confidence") or "0%"
        try:
            return float(str(value).replace("%", "").strip())
        except Exception:
            return 0.0

    def _duration(track):
        try:
            start = float(track.get("start_time_sec", 0.0))
            end = float(track.get("end_time_sec", 0.0))
            return max(0.0, end - start)
        except Exception:
            return 0.0

    def _score(track):
        # Confidence-first score with a small duration bonus.
        return _pct(track) + min(_duration(track), 10.0)

    def _same_or_unknown_direction(a, b):
        da = str(a.get("direction", "unknown")).lower()
        db = str(b.get("direction", "unknown")).lower()
        if da in {"unknown", "stationary"} or db in {"unknown", "stationary"}:
            return True
        # "indecisive" is noisy and often appears on short fragments.
        # Treat it as compatible to improve split-track merging.
        if da == "indecisive" or db == "indecisive":
            return True
        return da == db

    def _direction_pair(a, b):
        return (
            str(a.get("direction", "unknown")).lower(),
            str(b.get("direction", "unknown")).lower(),
        )

    def _frame_width(track):
        try:
            return float(track.get("frame_width", 640.0))
        except Exception:
            return 640.0

    def _x(track, key):
        try:
            return float(track.get(key, 0.0))
        except Exception:
            return 0.0

    def _continuity_threshold(track):
        width = max(1.0, _frame_width(track))
        return max(50.0, width * 0.18)

    def _merge_track_rows(a, b, merged_direction):
        # Keep chronology from the earlier segment and take best media/confidence
        # from whichever half saw the fish more clearly.
        a_start = float(a.get("start_time_sec", 0.0))
        b_start = float(b.get("start_time_sec", 0.0))
        first, second = (a, b) if a_start <= b_start else (b, a)

        merged = dict(first)
        merged["end_time_sec"] = second.get("end_time_sec", first.get("end_time_sec"))
        merged["direction"] = merged_direction
        merged["exit_x"] = second.get("exit_x", first.get("exit_x"))

        try:
            first_conf = float(first.get("best_conf_raw", 0.0))
        except Exception:
            first_conf = 0.0
        try:
            second_conf = float(second.get("best_conf_raw", 0.0))
        except Exception:
            second_conf = 0.0

        if second_conf > first_conf:
            for key in ("confidence", "avg_confidence", "best_crop", "best_frame", "species", "species_confidence"):
                if key in second:
                    merged[key] = second[key]
            merged["best_conf_raw"] = second.get("best_conf_raw", merged.get("best_conf_raw"))

        # Prefer the more confident timestamp if they disagree.
        first_ts_conf = str(first.get("timestamp_confidence", "")).upper()
        second_ts_conf = str(second.get("timestamp_confidence", "")).upper()
        if second_ts_conf == "HIGH" and first_ts_conf != "HIGH":
            merged["video_timestamp"] = second.get("video_timestamp", merged.get("video_timestamp"))
            merged["timestamp_confidence"] = second.get("timestamp_confidence", merged.get("timestamp_confidence"))

        return merged

    def _merged_direction_from_path(a, b):
        try:
            a_start = float(a.get("start_time_sec", 0.0))
            b_start = float(b.get("start_time_sec", 0.0))
        except Exception:
            a_start = 0.0
            b_start = 0.0

        first, second = (a, b) if a_start <= b_start else (b, a)
        entry_x = _x(first, "entry_x")
        exit_x = _x(second, "exit_x")
        width = max(_frame_width(first), _frame_width(second))
        min_net_dx = max(6.0, width * 0.02)
        net_dx = exit_x - entry_x

        if net_dx <= -min_net_dx:
            return "upstream"
        if net_dx >= min_net_dx:
            return "downstream"
        return "indecisive"

    def _is_turnaround_split(a, b):
        da, db = _direction_pair(a, b)
        if {da, db} != {"upstream", "downstream"}:
            return False

        if str(a.get("likely_class", "")).lower() != str(b.get("likely_class", "")).lower():
            return False

        a_start = float(a.get("start_time_sec", 0.0))
        a_end = float(a.get("end_time_sec", a_start))
        b_start = float(b.get("start_time_sec", 0.0))
        b_end = float(b.get("end_time_sec", b_start))
        if a_start <= b_start:
            earlier, later = a, b
            earlier_end, later_start = a_end, b_start
        else:
            earlier, later = b, a
            earlier_end, later_start = b_end, a_start

        gap = max(0.0, later_start - earlier_end)
        if gap > 1.50:
            return False

        early_entry = _x(earlier, "entry_x")
        early_exit = _x(earlier, "exit_x")
        later_entry = _x(later, "entry_x")
        later_exit = _x(later, "exit_x")

        width = max(_frame_width(earlier), _frame_width(later))
        side_boundary = width / 2.0
        continuity_threshold = max(_continuity_threshold(earlier), _continuity_threshold(later))

        # Turnaround pattern: the later fragment starts where the earlier fragment ended,
        # and the combined path exits near the same side where it originally entered.
        same_turn_point = abs(early_exit - later_entry) <= continuity_threshold
        same_outer_side = abs(early_entry - later_exit) <= max(continuity_threshold, width * 0.28)
        starts_and_ends_same_side = (
            (early_entry < side_boundary and later_exit < side_boundary)
            or (early_entry >= side_boundary and later_exit >= side_boundary)
        )

        return same_turn_point and same_outer_side and starts_and_ends_same_side

    def _is_opposite_direction_fragment(a, b):
        da, db = _direction_pair(a, b)
        if {da, db} != {"upstream", "downstream"}:
            return False

        if str(a.get("likely_class", "")).lower() != str(b.get("likely_class", "")).lower():
            return False

        a_start = float(a.get("start_time_sec", 0.0))
        a_end = float(a.get("end_time_sec", 0.0))
        b_start = float(b.get("start_time_sec", 0.0))
        b_end = float(b.get("end_time_sec", 0.0))
        gap = min(abs(a_start - b_end), abs(b_start - a_end))
        if gap > 0.60:
            return False

        a_ts = str(a.get("video_timestamp", "")).replace("*", "").strip()
        b_ts = str(b.get("video_timestamp", "")).replace("*", "").strip()
        if a_ts and b_ts and a_ts != b_ts:
            return False

        if _is_turnaround_split(a, b):
            return False

        continuity_threshold = max(_continuity_threshold(a), _continuity_threshold(b))
        a_entry = _x(a, "entry_x")
        a_exit = _x(a, "exit_x")
        b_entry = _x(b, "entry_x")
        b_exit = _x(b, "exit_x")

        close_exit_to_entry = (
            abs(a_exit - b_entry) <= continuity_threshold
            or abs(b_exit - a_entry) <= continuity_threshold
        )
        if not close_exit_to_entry:
            return False

        merged_direction = _merged_direction_from_path(a, b)
        return merged_direction in {"upstream", "downstream"}

    def _is_likely_duplicate(a, b):
        # Must be same class and either compatible direction or a clear turnaround split.
        if str(a.get("likely_class", "")).lower() != str(b.get("likely_class", "")).lower():
            return False
        if (
            not _same_or_unknown_direction(a, b)
            and not _is_turnaround_split(a, b)
            and not _is_opposite_direction_fragment(a, b)
        ):
            return False

        # If OCR timestamp resolves to the same second, treat close tracks as likely split IDs.
        a_ts = str(a.get("video_timestamp", "")).replace("*", "").strip()
        b_ts = str(b.get("video_timestamp", "")).replace("*", "").strip()

        a_start = float(a.get("start_time_sec", 0.0))
        a_end = float(a.get("end_time_sec", 0.0))
        b_start = float(b.get("start_time_sec", 0.0))
        b_end = float(b.get("end_time_sec", 0.0))

        overlap = max(0.0, min(a_end, b_end) - max(a_start, b_start))
        dur_a = _duration(a)
        dur_b = _duration(b)
        shorter = max(0.001, min(_duration(a), _duration(b)))
        overlap_ratio = overlap / shorter
        gap = min(abs(a_start - b_end), abs(b_start - a_end))

        # Strong overlap is a clear split-ID signal.
        if overlap_ratio >= 0.50:
            return True

        # Near-immediate handoff is also a strong split-ID signal.
        if gap <= 0.55:
            return True

        # Same OCR second + close in time is also a strong split-ID signal.
        if a_ts and b_ts and a_ts == b_ts and gap <= 2.00:
            return True

        # For looser gaps, only merge when one side is clearly a very short fragment.
        if gap <= 0.90 and min(dur_a, dur_b) <= 0.50:
            return True

        # Extra strict mode for one-fish videos: short fragment next to a longer track.
        if gap <= 1.50 and min(dur_a, dur_b) <= 0.80 and max(dur_a, dur_b) >= 1.20:
            return True

        return False

    ordered = sorted(finished_tracks, key=lambda t: float(t.get("start_time_sec", 0.0)))
    kept = []

    for track in ordered:
        merged = False
        for i, existing in enumerate(kept):
            if _is_likely_duplicate(existing, track):
                if _is_turnaround_split(existing, track):
                    kept[i] = _merge_track_rows(existing, track, "indecisive")
                elif _is_opposite_direction_fragment(existing, track):
                    kept[i] = _merge_track_rows(existing, track, _merged_direction_from_path(existing, track))
                elif _score(track) > _score(existing):
                    kept[i] = track
                merged = True
                break
        if not merged:
            kept.append(track)

    return kept

def dedupe_duplicate_track_ids(finished_tracks):
    """Collapse duplicate finalizations of the same DeepSort track ID."""
    if not finished_tracks:
        return finished_tracks

    def _parse_pct(value):
        try:
            return float(str(value).replace("%", "").strip())
        except Exception:
            return 0.0

    def _duration(track):
        try:
            start = float(track.get("start_time_sec", 0.0))
            end = float(track.get("end_time_sec", 0.0))
            return max(0.0, end - start)
        except Exception:
            return 0.0

    def _score(track):
        # Prefer higher confidence, then slightly prefer longer duration.
        conf = _parse_pct(track.get("avg_confidence") or track.get("confidence") or "0%")
        return conf + min(10.0, _duration(track))

    def _start(track):
        try:
            return float(track.get("start_time_sec", 0.0))
        except Exception:
            return 0.0

    def _end(track):
        try:
            return float(track.get("end_time_sec", _start(track)))
        except Exception:
            return _start(track)

    def _is_near_duplicate(a, b):
        # Same trackId can be legitimately reused later in a video.
        # Only merge rows that overlap or hand off almost immediately.
        a_start, a_end = _start(a), _end(a)
        b_start, b_end = _start(b), _end(b)
        overlap = max(0.0, min(a_end, b_end) - max(a_start, b_start))
        gap = max(0.0, max(a_start, b_start) - min(a_end, b_end))

        if overlap > 0.0:
            return True
        if gap <= 0.50:
            return True
        return False

    by_track = {}
    for track in sorted(finished_tracks, key=lambda t: _start(t)):
        tid = str(track.get("trackId", ""))
        by_track.setdefault(tid, []).append(track)

    kept = []
    for _, tracks in by_track.items():
        if not tracks:
            continue

        selected = [tracks[0]]
        for track in tracks[1:]:
            last = selected[-1]
            if _is_near_duplicate(last, track):
                if _score(track) > _score(last):
                    selected[-1] = track
            else:
                selected.append(track)

        kept.extend(selected)

    return sorted(kept, key=lambda t: _start(t))

def no_fish_found(video_path, filename):
    """Log that a video produced no fish tracks after all configured passes."""
    print("***************************************************************")
    print(f"No fish detected in {filename}. Skipping export.")
    print("***************************************************************")

def _append_no_fish_row(video_file_path, video_timestamp):
    """Append a no-fish result to the slim session file and the master CSV outputs."""
    slim_row = {
        "video_file": video_file_path,
        "location": FISHLENS_LOCATION,
        "video_timestamp": video_timestamp or "Not detected"
    }
    # Full-schema row for the master files; likely_class="no_fish" is the fish-present indicator.
    # Fish-specific fields are left blank so the master CSV has a uniform shape for every video.
    master_row = {k: "" for k in CSV_KEYS}
    master_row["video_file"]      = video_file_path
    master_row["likely_class"]    = "no_fish"
    master_row["video_timestamp"] = video_timestamp or "Not detected"
    master_row["location"]        = FISHLENS_LOCATION
    master_row["run"]             = _RUN_NAME

    try:
        # --- Session no-fish file (slim schema, wiped on startup) ---
        target = SESSION_NO_FISH_CSV
        if target:
            needs_header = not os.path.exists(target) or os.path.getsize(target) == 0
            with open(target, "a", newline="") as f:
                writer = csv.DictWriter(f, fieldnames=NO_FISH_CSV_KEYS)
                if needs_header:
                    writer.writeheader()
                writer.writerow(slim_row)

        # --- Master files (run_master + all_history) use the full CSV_KEYS schema. ---
        # session_fish + session_no_fish rolls up into run_master;
        # all run_master files roll up into all_history.
        if _RUN_FOLDER and _IS_DEBUG_RUN:
            # Debug: append no-fish row to debug.csv only, skip all_history
            _ensure_csv_schema(OUTPUT_CSV, CSV_KEYS, {"run": _RUN_NAME})
            needs_header = not os.path.exists(OUTPUT_CSV) or os.path.getsize(OUTPUT_CSV) == 0
            with open(OUTPUT_CSV, "a", newline="") as f:
                writer = csv.DictWriter(f, fieldnames=CSV_KEYS, extrasaction="ignore")
                if needs_header:
                    writer.writeheader()
                writer.writerow(master_row)
        elif _RUN_FOLDER:
            for path in (OUTPUT_CSV, MASTER_FISH_CSV):
                if path is None:
                    continue
                fill_values = {"run": _RUN_NAME} if path == OUTPUT_CSV else {}
                _ensure_csv_schema(path, CSV_KEYS, fill_values)
                needs_header = not os.path.exists(path) or os.path.getsize(path) == 0
                with open(path, "a", newline="") as f:
                    writer = csv.DictWriter(f, fieldnames=CSV_KEYS, extrasaction="ignore")
                    if needs_header:
                        writer.writeheader()
                    writer.writerow(master_row)
    except Exception as e:
        print(f"[ERROR] Failed to write no-fish row: {e}")

def save_best_image(finished_tracks, filename):
    """Save each track's best crop, classify it, and move it into a species subfolder."""
    for track in finished_tracks:
        best_crop = track.get("best_crop")

        if best_crop is not None:
            enhanced_crop = enhance_image(best_crop)
            
            # Classify the image first to determine species folder
            temp_image_name = f"{os.path.splitext(filename)[0]}_track_{track['trackId']}.jpg"
            temp_image_path = os.path.join(FISH_IMAGE_DIR, temp_image_name)
            write_ok = cv2.imwrite(temp_image_path, enhanced_crop, [cv2.IMWRITE_JPEG_QUALITY, 95])
            if not write_ok:
                print(f"Failed to write image at {temp_image_path}. Skipping classification.")
                track["species"] = "No data"
                track["species_confidence"] = "0.0000"
                track["image_path"] = None
                track.pop("best_crop", None)
                continue
            
            # Classify the saved image
            species_data = classify_image(temp_image_path)
            species = species_data[0] if species_data else "No data"
            track["species"] = species
            track["species_confidence"] = f"{(species_data[1] / 100):.4f}" if species_data and len(species_data) > 1 else "0.0000"
            
            # Create species subfolder and move image
            if species in CLASS_NAMES:
                species_folder = os.path.join(FISH_IMAGE_DIR, species)
                os.makedirs(species_folder, exist_ok=True)
                final_image_path = os.path.join(species_folder, temp_image_name)
                try:
                    shutil.move(temp_image_path, final_image_path)
                    track["image_path"] = final_image_path
                except Exception as e:
                    print(f"Error moving image to species folder: {e}")
                    track["image_path"] = None
                    try:
                        if os.path.exists(temp_image_path):
                            os.remove(temp_image_path)
                    except OSError:
                        pass
            else:
                # Do not keep unclassified images in root fish_images.
                track["image_path"] = None
                try:
                    if os.path.exists(temp_image_path):
                        os.remove(temp_image_path)
                except OSError:
                    pass
        else:
            track["image_path"] = None
        
        # Remove best_crop from track dict (no need to export it)
        track.pop("best_crop", None)

# ========================================================================
# CLASSIFICATION AND TIMESTAMP DEBUG HELPERS
# ========================================================================

def _safe_path_component(value, default="unknown"):
    """Convert arbitrary text into a filesystem-safe path segment."""
    text = str(value).strip() if value is not None else ""
    if not text:
        return default

    for ch in '<>:"/\\|?*':
        text = text.replace(ch, "_")
    return text or default

def _get_uncertain_timestamp_dir(source_video_path):
    """Build the output directory for uncertain-timestamp debug frames."""
    video_dir = os.path.basename(os.path.dirname(source_video_path)) or "root"

    return os.path.join(
        PROJECT_ROOT,
        "results",
        "uncertain_timestamps",
        _safe_path_component(video_dir, default="root")
    )

def save_uncertain_timestamp_frames(finished_tracks, source_video_path):
    """Save best-frame screenshots for tracks whose timestamps remain uncertain."""
    uncertain_dir = _get_uncertain_timestamp_dir(source_video_path)
    os.makedirs(uncertain_dir, exist_ok=True)
    
    for track in finished_tracks:
        timestamp_confidence = track.get("timestamp_confidence")
        best_frame = track.get("best_frame")
        
        # Save frame when timestamp is uncertain or not extracted.
        if timestamp_confidence != "HIGH" and best_frame is not None:
            video_name = os.path.splitext(os.path.basename(source_video_path))[0] or "unknown_video"
            safe_video_name = _safe_path_component(video_name, default="unknown_video")
            frame_filename = f"{safe_video_name}.png"
            frame_path = os.path.join(uncertain_dir, frame_filename)
            
            try:
                cv2.imwrite(frame_path, best_frame)
                print(f"Saved uncertain timestamp frame: {frame_path}")
            except Exception as e:
                print(f"Error saving uncertain timestamp frame: {e}")
        
        # Remove best_frame from track dict (no need to export it)
        track.pop("best_frame", None)

def classify_image(image_path):
    """Run the loaded species classifier on one saved crop image."""
   
    # Use the global model (already loaded at startup)
    model = CLASSIFIER_MODEL
    
    if model is None:
        print("Model was not loaded at startup. Skipping classification.")
        return ("No data", 0.0)

    if LOAD_IMG is None or IMG_TO_ARRAY is None:
        print("Keras image preprocessing utilities are unavailable. Skipping classification.")
        return ("No data", 0.0)
    
    try:
        # Load and preprocess image
        img = LOAD_IMG(image_path, target_size=IMAGE_SIZE)
        img_array = IMG_TO_ARRAY(img)
        if CLASSIFIER_PREPROCESS_MODE == "mobilenet_v2":
            img_array = (img_array / 127.5) - 1.0
        elif CLASSIFIER_PREPROCESS_MODE == "zero_one":
            img_array = img_array / 255.0
        img_array = np.expand_dims(img_array, axis=0)

      
        predictions = model.predict(img_array, verbose=0)
        
        # Get results
        pred_index = np.argmax(predictions)
        pred_label = CLASS_NAMES[pred_index]
        confidence = predictions[0][pred_index] * 100
        
        return pred_label, confidence
        
    except FileNotFoundError:
        print(f"File not found at {image_path}. Skipping.")
        return ("No data", 0.0)
    except Exception as e:
        print(f"ERROR during classification of {image_path}: {e}")
        return ("No data", 0.0)

# ========================================================================
# PUBLIC ENTRY POINT
# ========================================================================

def run_video_tracker(video_path, source_video_path=None):
    """Process one video through YOLO, DeepSort, timestamp OCR, and export prep."""

    # Initialize new VideoData and DeepSort tracker for each video
    vidData = VideoData()
    source_video_path = source_video_path or video_path
    # Always use the original source filename so image saves and logs show the real name,
    # not a temp MP4 path when an ASF/WMV was converted before analysis.
    vidData.v_filename = os.path.basename(source_video_path)
    tracker = DeepSortTracker()
    
    # Initialize frameData to avoid UnboundLocalError if video fails early
    frameData = None

    # Debug: Check file before attempting to open
    if not os.path.exists(video_path):
        print(f"[ERROR] File does not exist: {video_path}")
        return []
    


    # Open video, determine FPS, and read first frame
    cap = _video_capture_open(video_path)
    if not cap.isOpened():
        print(f"[ERROR] Could not open video with cv2.VideoCapture: {video_path}")
        print(f"[DEBUG] This usually means the video codec is not supported or file is corrupted")
        return []
    
    vidData.v_fps = cap.get(cv2.CAP_PROP_FPS) or FPS_DEFAULT
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT)) or 0
    frame_w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    if frame_w > 0:
        vidData.v_frame_width = frame_w
    ret, frame = _video_capture_read(cap)

    if ret and frame is not None:
        try:
            vidData.frame_width = int(frame.shape[1])
        except Exception:
            pass

    # Pre-compute a video-level timestamp fallback from early frames.
    # probe_video_timestamp now returns (timestamp, confidence).
    if ret and frame is not None:
        _probe_ts, _probe_conf = probe_video_timestamp(
            cap,
            frame,
            probe_frames=VIDEO_TIMESTAMP_PROBE_FRAMES,
            read_frame_fn=_video_capture_read
        )
        vidData.v_video_timestamp = _probe_ts
        vidData.v_probe_confidence = _probe_conf   # "HIGH" / "MEDIUM" / None
        ret, frame = _video_capture_read(cap)

    # Cycle through video frames until end of video
    while ret:
        vidData.v_current_track_ids = set()

        if frame is not None:
            try:
                vidData.frame_width = int(frame.shape[1])
            except Exception:
                pass

        # Speed mode: process every Nth frame
        if FRAME_STRIDE > 1 and (vidData.v_frame_index % FRAME_STRIDE) != 0:
            vidData.v_frame_index += 1
            vidData.v_total_frames += 1
            ret, frame = _video_capture_read(cap)
            continue

        # Initialize new FrameData object each frame.
        frameData = FrameData(f_index=vidData.v_frame_index)
        frameData.f_pos_ms = cap.get(cv2.CAP_PROP_POS_MSEC)
        frameData.f_detections = []

        analyze_yolo_detections(frame, MODEL, frameData, vidData)

        # Match increasing_accuracy ordering: classify YOLO frame results before DeepSort finalizes tracks.
        process_yolo_results(frameData, vidData, MODEL)

        # DeepSort Tracking
        deepsort_analysis(tracker, frame, frameData, vidData)

        # Finalize disappeared tracks (detections that ended before the video ended)
        finalize_tracks(frameData, vidData, termination_reason="disappeared")

        if vidData.v_frame_index % 100 == 0:
            total_display = str(total_frames) if total_frames > 0 else "?"
            print(f"[PROGRESS] FRAME:{vidData.v_frame_index}/{total_display}", flush=True)
        # Increment frame index and read next frame
        vidData.v_frame_index += 1
        vidData.v_total_frames += 1
        ret, frame = _video_capture_read(cap)

    # Finalize forced tracks (detections that were still active at the end of the video)
    if frameData is not None:
        finalize_tracks(frameData, vidData, termination_reason="forced")

    cap.release()

    print(f"[INFO] Processed {vidData.v_total_frames} frames, found {len(vidData.v_finished_tracks)} fish tracks")

    # Skip export if fish was not detected in this pass.
    # Do not copy to no_fish here; caller may still run a retry pass.
    if not vidData.v_found_fish:
        print(f"[INFO] No fish detected in {vidData.v_filename} (current pass)")
        return []

    # Timestamp dedupe can collapse distinct fish that share the same OCR second.
    # Keep it disabled in the default pipeline.
    # vidData.v_finished_tracks = dedupe_tracks_by_timestamp(vidData.v_finished_tracks)

    # Merge likely fragmented/split tracks of the same fish.
    vidData.v_finished_tracks = dedupe_fragmented_tracks(vidData.v_finished_tracks)

    # Guardrail: the same DeepSort trackId should not be exported multiple times per video.
    vidData.v_finished_tracks = dedupe_duplicate_track_ids(vidData.v_finished_tracks)

    # Save the best detection per video for analysis. TODO: Refactor based on edge cases (multiple fish?) and confidence threshold adjustments.
    if MAX_EXPORT_PER_VIDEO and len(vidData.v_finished_tracks) > MAX_EXPORT_PER_VIDEO:
        vidData.v_finished_tracks.sort(
            key=lambda x: float(x["avg_confidence"].replace("%", "")),
            reverse=True
        )

    # Save finalized tracks 
    vidData.v_finished_tracks = vidData.v_finished_tracks[:MAX_EXPORT_PER_VIDEO]

    print(f"[INFO] After dedupe/cap: {len(vidData.v_finished_tracks)} fish tracks")

    # Save the best image from each video for analysis.
    save_best_image(vidData.v_finished_tracks, os.path.basename(source_video_path))
    
    # Save frames for uncertain timestamps
    save_uncertain_timestamp_frames(vidData.v_finished_tracks, source_video_path)

    return vidData.v_finished_tracks

def _process_video_with_retry(video_path, source_video_path):
    """Two-pass processing: strict pass, then optional loose retry if fast mode may have skipped fish."""
    global FRAME_STRIDE, YOLO_CONFIDENCE_THRESHOLD, MIN_TRACK_DURATION_SEC

    original_stride = FRAME_STRIDE
    original_yolo_conf = YOLO_CONFIDENCE_THRESHOLD
    original_min_duration = MIN_TRACK_DURATION_SEC

    def _run_pass(label):
        print(
            f"[INFO] Starting {label} pass: "
            f"FRAME_STRIDE={FRAME_STRIDE}, "
            f"YOLO_CONF={YOLO_CONFIDENCE_THRESHOLD:.2f}, "
            f"MIN_TRACK_DURATION_SEC={MIN_TRACK_DURATION_SEC:.2f}"
        )
        try:
            tracks = run_video_tracker(video_path, source_video_path)
            return tracks if tracks else []
        except Exception as e:
            print(f"[WARN] {label} pass failed: {e}")
            return []

    try:
        # Pass 1: strict settings
        FRAME_STRIDE = STRICT_FRAME_STRIDE
        YOLO_CONFIDENCE_THRESHOLD = STRICT_YOLO_CONFIDENCE_THRESHOLD
        MIN_TRACK_DURATION_SEC = STRICT_MIN_TRACK_DURATION_SEC
        video_tracks = _run_pass("strict")

        # Pass 2: loose settings with FRAME_STRIDE=1 whenever pass 1 found no fish.
        if not video_tracks and ENABLE_LOOSE_RETRY:
            print(
                f"[INFO] No fish found with strict settings (FRAME_STRIDE={STRICT_FRAME_STRIDE}); "
                "retrying once with FRAME_STRIDE=1 and loose thresholds"
            )
            FRAME_STRIDE = 1
            YOLO_CONFIDENCE_THRESHOLD = LOOSE_YOLO_CONFIDENCE_THRESHOLD
            MIN_TRACK_DURATION_SEC = LOOSE_MIN_TRACK_DURATION_SEC
            video_tracks = _run_pass("loose")

        # Only mark/copy as no-fish after all passes are exhausted.
        if not video_tracks:
            no_fish_found(source_video_path or video_path, os.path.basename(source_video_path or video_path))

        # Second pass (ffmpeg/ffprobe): enrich export with display duration only.
        # No changes to track creation, filtering, dedupe, or existing fields.
        _enrich_tracks_with_duration(video_tracks, video_path, source_video_path)

        return video_tracks
    finally:
        FRAME_STRIDE = original_stride
        YOLO_CONFIDENCE_THRESHOLD = original_yolo_conf
        MIN_TRACK_DURATION_SEC = original_min_duration

def main(input_path=None):
    """Process either one video or every video in a folder and flush results per video."""
    if input_path is None:
        input_path = os.path.join(PROJECT_ROOT, "SavedVids")

    if os.path.isfile(input_path):
        video_folder = os.path.dirname(input_path)
        single_video_file = input_path
    else:
        video_folder = input_path
        single_video_file = None

    os.makedirs(video_folder, exist_ok=True)

    # Debug: Print paths
    print(f"[INFO] Processing videos from: {video_folder}")

    # Process all videos in folder
    print(f"Performance: FAST_MODE={FAST_MODE}, FRAME_STRIDE={FRAME_STRIDE}, YOLO_IMGSZ={YOLO_IMGSZ}")
    if single_video_file:
        # Process single video file
        print(f"[PROGRESS] TOTAL:1", flush=True)
        video_path, is_temp = convert_asf_to_mp4(single_video_file)
        filename = os.path.basename(single_video_file)
        print(f"[PROGRESS] VIDEO:1/1|{filename}", flush=True)
        try:
            video_tracks = _process_video_with_retry(video_path, single_video_file)
        finally:
            if is_temp:
                _cleanup_temp(video_path)

        for t in video_tracks:
            t["video_file"] = single_video_file
            t["location"] = FISHLENS_LOCATION
            t["run"] = _RUN_NAME
        if video_tracks:
            try:
                _flush_tracks_to_csv(video_tracks)
                print(f"[SUCCESS] Exported {len(video_tracks)} fish tracks for {filename}.")
            except Exception as e:
                print(f"[ERROR] Failed to write CSV for {filename}: {e}")
        else:
            print(f"[INFO] No fish tracks to export for {filename}.")
        print(f"[PROGRESS] VIDEO_DONE:{filename}", flush=True)
    else:
        # Process all videos in folder
        video_extensions = ('.mp4', '.avi', '.mov', '.mkv', '.asf', '.wmv', '.flv', '.webm')
        files_in_folder = []
        
        try:
            files_in_folder = os.listdir(video_folder)
            print(f"[INFO] Found {len(files_in_folder)} items in folder")
        except Exception as e:
            print(f"[ERROR] Failed to list video folder: {e}")
            return
        
        video_files = [
            f for f in files_in_folder
            if os.path.isfile(os.path.join(video_folder, f)) and f.lower().endswith(video_extensions)
        ]
        video_count = len(video_files)
        print(f"[PROGRESS] TOTAL:{video_count}", flush=True)

        # Build set of already-analyzed filenames from run_master.csv so they can be skipped.
        # Set FISHLENS_FORCE_REANALYZE=1 to bypass this check and re-process all videos.
        FORCE_REANALYZE = os.getenv("FISHLENS_FORCE_REANALYZE", "0") == "1"
        already_analyzed = set()
        if not FORCE_REANALYZE:
            master_csv_path = OUTPUT_CSV  # run_master.csv
            if master_csv_path and os.path.exists(master_csv_path):
                try:
                    with open(master_csv_path, newline="") as _f:
                        for row in csv.DictReader(_f):
                            stored = row.get("video_file", "")
                            if stored:
                                already_analyzed.add(os.path.basename(stored))
                except Exception as _e:
                    print(f"[WARNING] Could not read master CSV for skip-check: {_e}")
        else:
            print("[INFO] FORCE_REANALYZE=1: skipping already-analyzed check, all videos will be re-processed.", flush=True)

        for video_index, filename in enumerate(video_files, start=1):
            item_path = os.path.join(video_folder, filename)
            print(f"[PROGRESS] VIDEO:{video_index}/{video_count}|{filename}", flush=True)

            if filename in already_analyzed:
                print(f"[INFO] Skipping (already analyzed): {filename}", flush=True)
                continue

            video_path, is_temp = convert_asf_to_mp4(item_path)
            print(f"Processing: {filename}")
            try:
                video_tracks = _process_video_with_retry(video_path, item_path)
            finally:
                if is_temp:
                    _cleanup_temp(video_path)
            for t in video_tracks:
                t["video_file"] = item_path
                t["location"] = FISHLENS_LOCATION
                t["run"] = _RUN_NAME

            # Flush this video's tracks to CSV immediately so results are preserved
            # even if the run is cancelled before all videos finish.
            if video_tracks:
                try:
                    _flush_tracks_to_csv(video_tracks)
                    print(f"[SUCCESS] Exported {len(video_tracks)} fish tracks for {filename}.")
                except Exception as e:
                    print(f"[ERROR] Failed to write CSV for {filename}: {e}")
            else:
                print(f"[INFO] No fish tracks to export for {filename}.")

            print(f"[PROGRESS] VIDEO_DONE:{filename}", flush=True)

# Persistent server loop: accept one folder path per line from stdin, process it, signal done.
if CLI_INPUT_PATH:
    try:
        main(CLI_INPUT_PATH)
    except Exception as e:
        print(f"[ERROR] Unhandled exception during processing: {e}", flush=True)
        import traceback
        traceback.print_exc()
    print("[PROGRESS] DONE", flush=True)
else:
    for raw_line in sys.stdin:
        input_path = raw_line.strip()
        if input_path:
            try:
                main(input_path)
            except Exception as e:
                print(f"[ERROR] Unhandled exception during processing: {e}", flush=True)
                import traceback
                traceback.print_exc()
            print("[PROGRESS] DONE", flush=True)
