# ****************************************************************
# File: main.py
# Description: Main video processing script for YOLO and DeepSort.
# Author: Aden
# Contributers: Aleks, Reid
# Notes: N/A
# ****************************************************************

import sys
print("[PROGRESS] STARTUP", flush=True)

import warnings
import os
import csv
import cv2
import subprocess
import importlib
from contextlib import contextmanager
import tempfile
import numpy as np
import shutil
#from YOLO.detector import YoloDetector
from tracking.deepsort_tracker import DeepSortTracker
from collections import Counter
from dataclasses import dataclass, field
from typing import List
from extract_timestamp import extractTimestamFromFrame, check_tesseract, probe_video_timestamp, dedupe_tracks_by_timestamp

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
    try:
        ultralytics = importlib.import_module("ultralytics")
        return ultralytics.YOLO(model_path)
    except Exception as e:
        print(f"WARNING: Could not load YOLO model '{model_path}': {e}")
        return None


def _load_classifier_model(model_path):
    try:
        keras_models = importlib.import_module("keras.models")
        return keras_models.load_model(model_path)
    except Exception as e:
        print(f"WARNING: Could not load classifier model '{model_path}': {e}")
        return None


def _load_keras_image_utils():
    try:
        keras_image = importlib.import_module("keras.preprocessing.image")
        return keras_image.load_img, keras_image.img_to_array
    except Exception:
        return None, None


def _resolve_classifier_model_path():
    candidates = [
        os.path.join(PROJECT_ROOT, "models", "fish_classifier_model.h5"),
        os.path.join(PROJECT_ROOT, "fish_classifier_model.h5")
    ]
    for path in candidates:
        if os.path.exists(path):
            return path
    # Keep old default if none found so warning message still shows attempted path.
    return candidates[0]

# Constants--Folders and Directories
PROJECT_ROOT = os.path.dirname(os.path.abspath(__file__))


# Constants--General
CSV_KEYS = [
    "video_file",
    "trackId",
    "image_path",
    "likely_class",
    "confidence",
    "start_time_sec",
    "end_time_sec",
    "avg_confidence",
    "direction",
    "species",
    "species_confidence",
    "video_timestamp",
    "location"
]
NO_FISH_CSV_KEYS = ["video_file", "location", "video_timestamp"]
TESSERACT_AVAILABLE = check_tesseract()

# Constants--YOLO
MODEL = _load_yolo_model("models/fish_detector2.pt")
YOLO_CONFIDENCE_THRESHOLD = 0.25  # Adjustable: lower = detects more fish (but more false positives), higher = more selective
NO_FISH = os.path.join(PROJECT_ROOT, "no_fish")

# Constants--DeepSort
FPS_DEFAULT = 30 
MAX_EXPORT_PER_VIDEO = 5  
OUTPUT_CSV = os.path.join(PROJECT_ROOT, "fish_summary.csv")
NO_FISH_CSV = os.path.join(PROJECT_ROOT, "no_fish_summary.csv")
FISH_IMAGE_DIR = os.path.join(PROJECT_ROOT, "fish_images")

# Performance tuning
# FISHLENS_FAST_MODE is set by the app via the Fast Mode setting in Settings.
# When enabled: skips every other frame and uses lower YOLO resolution.
FAST_MODE = os.getenv("FISHLENS_FAST_MODE", "0") == "1"
FRAME_STRIDE = max(1, int(os.getenv("FISHLENS_FRAME_STRIDE", "2" if FAST_MODE else "1")))
YOLO_IMGSZ = max(320, int(os.getenv("FISHLENS_YOLO_IMGSZ", "448" if FAST_MODE else "512")))
SAVE_TIMESTAMP_DEBUG_FRAMES = os.getenv("FISHLENS_SAVE_TIMESTAMP_DEBUG", "0") == "1"
TIMESTAMP_MAX_ATTEMPTS = max(1, int(os.getenv("FISHLENS_TIMESTAMP_MAX_ATTEMPTS", "4" if FAST_MODE else "8")))
SUPPRESS_CODEC_WARNINGS = os.getenv("FISHLENS_SUPPRESS_CODEC_WARNINGS", "1") == "1"
VIDEO_TIMESTAMP_PROBE_FRAMES = max(1, int(os.getenv("FISHLENS_VIDEO_TS_PROBE_FRAMES", "6" if FAST_MODE else "12")))
FISHLENS_LOCATION = os.getenv("FISHLENS_LOCATION", "Unknown")

# Constants--Classifier
CLASSIFIER_MODEL_PATH = _resolve_classifier_model_path()
CLASSIFIER_MODEL = _load_classifier_model(CLASSIFIER_MODEL_PATH)
LOAD_IMG, IMG_TO_ARRAY = _load_keras_image_utils()
CLASS_NAMES = ["Chinook", "Omykiss"]
IMAGE_SIZE = (150, 150)

# Create and initialize folders
os.makedirs(NO_FISH, exist_ok=True)
os.makedirs(FISH_IMAGE_DIR, exist_ok=True)

# Signal to the host application that models are loaded and we are ready for work.
print("[PROGRESS] READY", flush=True)

@dataclass
class FrameData:
        f_index: int = 0
        f_found_fish: bool = False 
        f_detections: List = field(default_factory=list) 

class VideoData:
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
        self.v_frame_width = 640


@contextmanager
def _suppress_stderr(enabled=True):
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
    with _suppress_stderr(SUPPRESS_CODEC_WARNINGS):
        return cv2.VideoCapture(video_path)


def _video_capture_read(cap):
    return cap.read()


# ****************************************************************
# Function: main
# Description: Process all videos and export data as a CSV.
# Notes: N/A
def main(input_path=None):
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

    # Clear no-fish CSV fresh for each run
    try:
        with open(NO_FISH_CSV, "w", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=NO_FISH_CSV_KEYS)
            writer.writeheader()
    except Exception as e:
        print(f"[WARNING] Could not initialize no-fish CSV: {e}")

    # Process all videos in folder
    all_tracks = []
    print(f"Performance: FAST_MODE={FAST_MODE}, FRAME_STRIDE={FRAME_STRIDE}, YOLO_IMGSZ={YOLO_IMGSZ}")
    if single_video_file:
        # Process single video file
        print(f"[PROGRESS] TOTAL:1", flush=True)
        video_path, is_temp = convert_asf_to_mp4(single_video_file)
        filename = os.path.basename(single_video_file)
        print(f"[PROGRESS] VIDEO:1/1|{filename}", flush=True)
        try:
            video_tracks = run_video_tracker(video_path, single_video_file)
        finally:
            if is_temp:
                _cleanup_temp(video_path)

        for t in video_tracks:
            t["video_file"] = single_video_file
            t["location"] = FISHLENS_LOCATION
        all_tracks.extend(video_tracks)
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

        for video_index, filename in enumerate(video_files, start=1):
            item_path = os.path.join(video_folder, filename)
            print(f"[PROGRESS] VIDEO:{video_index}/{video_count}|{filename}", flush=True)
            video_path, is_temp = convert_asf_to_mp4(item_path)
            print(f"Processing: {filename}")
            try:
                video_tracks = run_video_tracker(video_path, item_path)
            finally:
                if is_temp:
                    _cleanup_temp(video_path)
            for t in video_tracks:
                t["video_file"] = item_path
                t["location"] = FISHLENS_LOCATION
            all_tracks.extend(video_tracks)

    # Export CSV
    if all_tracks:
        try:
            with open(OUTPUT_CSV, "w", newline="") as f:
                writer = csv.DictWriter(f, fieldnames=CSV_KEYS, extrasaction="ignore")
                writer.writeheader()
                writer.writerows(all_tracks)
            print(f"[SUCCESS] Exported {len(all_tracks)} fish tracks to {OUTPUT_CSV}")
        except Exception as e:
            print(f"[ERROR] Failed to write CSV: {e}")
    else:
        print("[INFO] No fish tracks to export.")

# ****************************************************************
# Function: enhance_image
# Description: Enhance image quality through upscaling and sharpening.
# Notes: N/A
def enhance_image(crop):
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


# ****************************************************************
# Function: convert_asf_to_mp4
# Description: Convert ASF video to a temporary MP4 to improve decode reliability.
# Notes: Returns (path, is_temp). Caller is responsible for deleting the temp file.
def convert_asf_to_mp4(video_path):
    if not video_path.lower().endswith('.asf'):
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

    fps = cap.get(cv2.CAP_PROP_FPS) or FPS_DEFAULT
    if fps <= 0:
        fps = FPS_DEFAULT

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
    try:
        if path and os.path.exists(path):
            os.remove(path)
    except OSError:
        pass

# ****************************************************************
# Function: run_video_tracker
# Description: Process a single video through both YOLO and DeepSort;
# return tracked fish data.
# Notes: N/A
def run_video_tracker(video_path, source_video_path=None):

    # Initialize new VideoData and DeepSort tracker for each video
    vidData = VideoData()
    source_video_path = source_video_path or video_path
    vidData.v_filename = os.path.basename(video_path)
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

    # Pre-compute a video-level timestamp fallback from early frames.
    if ret and frame is not None:
        vidData.v_video_timestamp = probe_video_timestamp(
            cap,
            frame,
            probe_frames=VIDEO_TIMESTAMP_PROBE_FRAMES,
            read_frame_fn=_video_capture_read
        )
        ret, frame = _video_capture_read(cap)

    # Cycle through video frames until end of video
    while ret:

        # Speed mode: process every Nth frame
        if FRAME_STRIDE > 1 and (vidData.v_frame_index % FRAME_STRIDE) != 0:
            vidData.v_frame_index += 1
            vidData.v_total_frames += 1
            ret, frame = _video_capture_read(cap)
            continue

        # Initialize new FrameData object each frame
        frameData = FrameData(f_index=vidData.v_frame_index)
        frameData.f_detections = []

        # Determine most common class per-frame
        analyze_yolo_detections(frame, MODEL, frameData, vidData)

        # Reset per-frame track set before DeepSort populates it
        vidData.v_current_track_ids.clear()

        # DeepSort Tracking
        deepsort_analysis(tracker, frame, frameData, vidData)

        # YOLO post-processing runs after DeepSort overlap filtering
        process_yolo_results(frameData, vidData, MODEL)

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

    # Skip export if fish was not detected in video
    if not vidData.v_found_fish:
        print(f"[INFO] No fish detected in {vidData.v_filename}")
        no_fish_found(video_path, vidData.v_filename)
        _append_no_fish_row(source_video_path, vidData.v_video_timestamp)
        return []

    # Remove duplicate tracks that share the exact same timestamp.
    # Keep the one with higher confidence.
    vidData.v_finished_tracks = dedupe_tracks_by_timestamp(vidData.v_finished_tracks)

    # Merge likely fragmented/split tracks of the same fish.
    vidData.v_finished_tracks = dedupe_fragmented_tracks(vidData.v_finished_tracks)

    # Save the best detection per video for analysis. TODO: Refactor based on edge cases (multiple fish?) and confidence threshold adjustments.
    if MAX_EXPORT_PER_VIDEO and len(vidData.v_finished_tracks) > MAX_EXPORT_PER_VIDEO:
        vidData.v_finished_tracks.sort(
            key=lambda x: float(x["avg_confidence"].replace("%", "")),
            reverse=True
        )

    # Save finalized tracks 
    vidData.v_finished_tracks = vidData.v_finished_tracks[:MAX_EXPORT_PER_VIDEO]

    # Save the best image from each video for analysis.
    save_best_image(vidData.v_finished_tracks, vidData.v_filename)
    
    # Save frames for uncertain timestamps
    save_uncertain_timestamp_frames(vidData.v_finished_tracks, source_video_path)

    return vidData.v_finished_tracks


# ****************************************************************
# Function: analyse_yolo_detections
# Description: Analyze YOLO detections to determine most common class of frame.
# Notes: Vars modified: found_fish (frame, video), detections(frame), frames with/without fish (video)
def analyze_yolo_detections(frame, model, frameData, vidData):

    # Run YOLO on frame
    results = model.predict(
        source=frame,
        verbose=False,
        stream=False,
        save=False,
        imgsz=YOLO_IMGSZ
    )

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
            
            if box_area < 100:
                continue

            # Mark if YOLO detected a fish in frame.
            try:
                cls_name = model.names[cls_id].lower()
            except Exception:
                cls_name = str(cls_id)
            if "fish" in cls_name:
                frameData.f_found_fish = True
                detection_count += 1
            frameData.f_detections.append([x1, y1, x2, y2, conf, cls_id])
        

    
    # Increment video-level stats based on frame-level results
    vidData.v_found_fish = vidData.v_found_fish or frameData.f_found_fish
        
    vidData.v_frames_with_fish += 1 if frameData.f_found_fish else 0
    vidData.v_frames_without_fish += 0 if frameData.f_found_fish else 1


# ****************************************************************
# Function: process_yolo_results
# Description: Process YOLO results to determine most common class and average confidence.
# Notes: N/A
def process_yolo_results(frameData, vidData, model):
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
            
# ***************************************************************
# Function: deepsort_analysis
# Description: Run video through DeepSort and return track data.
# Notes: N/A
def deepsort_analysis(tracker, frame, frameData, vidData):

    # Use the tracker's default iou threshold (tuned in the tracker)
    frameData.f_detections = tracker.filterOverlaps(frameData.f_detections)
    tracked_objects = tracker.update(frameData.f_detections, frame)
    
    for obj in tracked_objects:
        trackId = obj["trackId"]
        vidData.v_current_track_ids.add(trackId)

        # Check if this is a newtrack
        is_new_track = trackId not in vidData.v_active_tracks

        if is_new_track:
            x1_e, y1_e, x2_e, y2_e = obj["bbox"]
            initial_conf = "LOW" if (vidData.v_video_timestamp and vidData.v_video_timestamp != "Not detected") else None
            vidData.v_active_tracks[trackId] = {
                "start_frame": frameData.f_index,
                "confidences": [],
                "directions": [],
                "best_conf": -1.0,
                "best_crop": None,
                "entry_x": (x1_e + x2_e) / 2,
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
        
        # Keep trying to get direct OCR if we only have LOW confidence (from probe) or no timestamp at all
        if current_confidence in (None, "LOW"):
            attempts = track_data.get("timestamp_attempts", 0)
            if attempts < TIMESTAMP_MAX_ATTEMPTS:
                result = extractTimestamFromFrame(frame, False)
                track_data["timestamp_attempts"] = attempts + 1

                if result and result[0]:  # result is (timestamp, confidence) tuple
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
            if conf > vidData.v_active_tracks[trackId]["best_conf"]:
                vidData.v_active_tracks[trackId]["best_conf"] = conf
                vidData.v_active_tracks[trackId]["best_crop"] = crop.copy()
                vidData.v_active_tracks[trackId]["best_frame"] = frame.copy()


# ****************************************************************
# Function: finalize_tracks
# Description: Finalizes ends of tracks.
# Notes: termination_reason can be:
# "disappeared" (normal end of track), or,
# "forced" (end of video with active tracks).
def finalize_tracks(frameData, vidData, termination_reason): 

    if termination_reason not in ("disappeared", "forced"):
        raise ValueError("Invalid termination reason. Must be 'disappeared' or 'forced'.")
    
    if termination_reason == "disappeared":         
        disappeared_ids = set(vidData.v_active_tracks.keys()) - vidData.v_current_track_ids 
        for tid in disappeared_ids:
            track_data = vidData.v_active_tracks.pop(tid)
            track_dict = build_track_summary(tid, track_data, frameData, vidData)
            if track_dict:
                vidData.v_finished_tracks.append(track_dict)

    elif termination_reason == "forced":
        for tid, track_data in vidData.v_active_tracks.items():
            track_dict = build_track_summary(tid, track_data, frameData, vidData)
            if track_dict:
                vidData.v_finished_tracks.append(track_dict)


# ****************************************************************
# Function: build_track_summary
# Description: Helper function for finalizing track data.
# Notes: N/A
def build_track_summary(trackId, track_data, frameData, vidData, image_path=None):
    duration_sec = (frameData.f_index - track_data["start_frame"]) / vidData.v_fps
    if duration_sec < 1.0:
        return None
    
    # Calculate DeepSort average confidence
    confidences = [c for c in track_data["confidences"] if c is not None]
    avg_conf_DS = sum(confidences) / len(confidences) if confidences else 0.0
    
    # Get best confidence for track
    best_conf = track_data.get("best_conf", 0.0)
    best_conf_pct = best_conf * 100 if best_conf <= 1.0 else best_conf
    
    # Determine direction.
    # Uses net displacement (exit_x - entry_x) cross-checked against per-frame DeepSort counts.
    # The center-line "left/right side" approach is unreliable because DeepSort's n_init=10 means
    # entry_x is recorded ~10 frames in — a fast fish may already be past center by then.
    entry_x = track_data.get("entry_x", vidData.v_frame_width / 2)
    exit_x = track_data.get("last_x", vidData.v_frame_width / 2)
    net_dx = exit_x - entry_x  # positive = fish moved right (downstream) overall

    directions = track_data["directions"]
    upstream_count = directions.count("upstream")
    downstream_count = directions.count("downstream")
    total_directional = upstream_count + downstream_count

    if total_directional == 0:
        # Only stationary frames — no directional movement.
        overall_direction = directions[-1] if directions else "unknown"
    elif downstream_count > upstream_count:
        # Majority of frames were downstream; net displacement must also be rightward to confirm.
        # If net_dx contradicts the frame counts, movement was genuinely ambiguous.
        overall_direction = "downstream" if net_dx >= 0 else "indecisive"
    elif upstream_count > downstream_count:
        overall_direction = "upstream" if net_dx <= 0 else "indecisive"
    else:
        # Equal upstream and downstream frame counts → genuinely ambiguous.
        overall_direction = "indecisive"
    
    # Handle timestamp with confidence flag
    video_timestamp = track_data.get("video_timestamp") or vidData.v_video_timestamp or "Not detected"
    timestamp_confidence = track_data.get("timestamp_confidence")
    
    # Add * only if timestamp is LOW confidence
    if timestamp_confidence and timestamp_confidence == "LOW":
        video_timestamp = f"{video_timestamp}*"
    
    return {
        "trackId": trackId,
        "likely_class": vidData.v_most_common_class,
        "confidence": f"{best_conf_pct:.2f}%" if best_conf_pct > 0 else f"{vidData.v_avg_confidence_YL:.2f}%",
        "avg_confidence": f"{best_conf_pct:.2f}%" if best_conf_pct > 0 else f"{avg_conf_DS:.2f}%",
        "start_time_sec": f"{track_data['start_frame'] / vidData.v_fps:.2f}",
        "end_time_sec": f"{frameData.f_index / vidData.v_fps:.2f}",
        "direction": overall_direction,
        "best_crop": track_data.get("best_crop"),
        "species": "No data",
        "species_confidence": "No data",
        "video_timestamp": video_timestamp,
        "timestamp_confidence": timestamp_confidence,
        "best_frame": vidData.v_active_tracks.get(trackId, {}).get("best_frame") if trackId in vidData.v_active_tracks else None
    }


# ****************************************************************
# Function: dedupe_fragmented_tracks
# Description: Merge likely duplicate tracks caused by tracker ID splits.
# Notes: Conservative heuristic to reduce false double-fish reports.
def dedupe_fragmented_tracks(finished_tracks):
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
        if da in ("unknown", "stationary", "indecisive") or db in ("unknown", "stationary", "indecisive"):
            return True
        return da == db

    def _is_likely_duplicate(a, b):
        # Must be same class and compatible direction.
        if str(a.get("likely_class", "")).lower() != str(b.get("likely_class", "")).lower():
            return False
        if not _same_or_unknown_direction(a, b):
            return False

        a_start = float(a.get("start_time_sec", 0.0))
        a_end = float(a.get("end_time_sec", 0.0))
        b_start = float(b.get("start_time_sec", 0.0))
        b_end = float(b.get("end_time_sec", 0.0))

        overlap = max(0.0, min(a_end, b_end) - max(a_start, b_start))
        shorter = max(0.001, min(_duration(a), _duration(b)))
        overlap_ratio = overlap / shorter
        gap = min(abs(a_start - b_end), abs(b_start - a_end))

        # Temporal relationship expected for split IDs.
        # 0.25 overlap ratio catches heavily-overlapping duplicate tracks;
        # 1.0s gap allows for brief detection dropout between the same fish.
        temporal_match = (overlap_ratio >= 0.25) or (gap <= 1.0)
        if not temporal_match:
            return False

        # Prefer merging only when one track is clearly weaker/shorter.
        pa = _pct(a)
        pb = _pct(b)
        conf_gap = abs(pa - pb)
        dur_ratio = min(_duration(a), _duration(b)) / max(0.001, max(_duration(a), _duration(b)))

        return conf_gap >= 8.0 or dur_ratio <= 0.45

    ordered = sorted(finished_tracks, key=lambda t: float(t.get("start_time_sec", 0.0)))
    kept = []

    for track in ordered:
        merged = False
        for i, existing in enumerate(kept):
            if _is_likely_duplicate(existing, track):
                if _score(track) > _score(existing):
                    kept[i] = track
                merged = True
                break
        if not merged:
            kept.append(track)

    return kept


# ***************************************************************
# Function: no_fish_found
# Description: Moves video to no_fish if no fish detected. 
# Notes:
def no_fish_found(video_path, filename):
    print("***************************************************************")
    print(f"No fish detected in {filename}. Skipping export.")
    print("***************************************************************")

    log_path = os.path.join(NO_FISH, "no_fish_log.txt")
    try:
        with open(log_path, "a") as log_file:
            log_file.write(f"{filename} | {video_path}\n")
    except Exception as e:
        print(f"Error writing no-fish log: {e}")


# ***************************************************************
# Function: _append_no_fish_row
# Description: Appends one row to no_fish_summary.csv for a video with no detections.
# Notes: N/A
def _append_no_fish_row(video_file_path, video_timestamp):
    row = {
        "video_file": video_file_path,
        "location": FISHLENS_LOCATION,
        "video_timestamp": video_timestamp or "Not detected"
    }
    try:
        with open(NO_FISH_CSV, "a", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=NO_FISH_CSV_KEYS)
            writer.writerow(row)
    except Exception as e:
        print(f"[ERROR] Failed to write no-fish row: {e}")


# ***************************************************************
# Function: save_best_image
# Description: Saves the best image from each video. 
# Notes:
def save_best_image(finished_tracks, filename):
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
                    track["species_confidence"] = "No data"
                    track["image_path"] = None
                    track.pop("best_crop", None)
                    continue
                
                # Classify the saved image
                species_data = classify_image(temp_image_path)
                species = species_data[0] if species_data else "No data"
                track["species"] = species
                track["species_confidence"] = f"{species_data[1]:.2f}%" if species_data and len(species_data) > 1 else "No data"
                
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


# ****************************************************************
# Function: _safe_path_component
# Description: Convert arbitrary text into a filesystem-safe path segment.
# Notes: N/A
def _safe_path_component(value, default="unknown"):
    text = str(value).strip() if value is not None else ""
    if not text:
        return default

    for ch in '<>:"/\\|?*':
        text = text.replace(ch, "_")
    return text or default


# ****************************************************************
# Function: _get_uncertain_timestamp_dir
# Description: Build output directory for uncertain timestamp frames.
# Notes: Format: results/uncertain_timestamps/<video_directory>
def _get_uncertain_timestamp_dir(source_video_path):
    video_dir = os.path.basename(os.path.dirname(source_video_path)) or "root"

    return os.path.join(
        PROJECT_ROOT,
        "results",
        "uncertain_timestamps",
        _safe_path_component(video_dir, default="root")
    )


# ****************************************************************
# Function: save_uncertain_timestamp_frames
# Description: Save frame screenshots for tracks with uncertain (non-HIGH) timestamps.
# Notes: Includes LOW confidence and failed timestamp extraction (None / Not detected).
def save_uncertain_timestamp_frames(finished_tracks, source_video_path):
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


# ****************************************************************
# Function: classify_image
# Description: Loads, preprocesses, and classifies a single image file.
# Notes: Accepts image_path parameter for the image to classify.
def classify_image(image_path):
   
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


# Persistent server loop: accept one folder path per line from stdin, process it, signal done.
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
