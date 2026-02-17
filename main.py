# ****************************************************************
# File: main.py
# Description: Main video processing script for YOLO and DeepSort.
# Author: Aden
# Contributers: Aleks, Reid
# Notes: N/A
# ****************************************************************

import warnings
from fileinput import filename
import os
import csv
import cv2
import numpy as np
from ultralytics import YOLO
import shutil
import sys
#from YOLO.detector import YoloDetector
from tracking.deepsort_tracker import DeepSortTracker
from collections import Counter
from pathlib import Path
import numpy as np
from keras.models import load_model
from dataclasses import dataclass, field
from typing import List
from keras.preprocessing.image import load_img, img_to_array

# Suppress deprecation warning from pkg_resources
warnings.filterwarnings(
    "ignore",
    category=UserWarning,
    message="pkg_resources is deprecated"
)

# Constants--Folders and Directories
PROJECT_ROOT = os.path.dirname(os.path.abspath(__file__))
VIDEO_FOLDER = sys.argv[1] if len(sys.argv) > 1 else os.path.join(PROJECT_ROOT, "sample_data") 

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
    "species_confidence"
]

# Constants--YOLO
MODEL = YOLO("models/fish_detector.pt")
NO_FISH = "no_fish"

# Constants--DeepSort
FPS_DEFAULT = 30 
MAX_EXPORT_PER_VIDEO = 5  
OUTPUT_CSV = "fish_summary.csv"
FISH_IMAGE_DIR = "fish_images"

# Constants--Classifier
CLASSIFIER_MODEL_PATH = os.path.join(PROJECT_ROOT, "fish_classifier_model.h5")
CLASSIFIER_TARGET_FOLDER = "images"
CLASS_NAMES = ["Chinook", "Omykiss"] 
IMAGE_SIZE = (150, 150)
IMAGE_EXTS = {'.jpg', '.jpeg', '.png'}

# Create and initialize folders
os.makedirs(VIDEO_FOLDER, exist_ok=True)
os.makedirs(NO_FISH, exist_ok=True)
os.makedirs(FISH_IMAGE_DIR, exist_ok=True)

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


# ****************************************************************
# Function: main
# Description: Process all videos and export data as a CSV.
# Notes: N/A
def main():

    # Process all videos in folder
    all_tracks = []
    for filename in os.listdir(VIDEO_FOLDER):
        video_path = os.path.join(VIDEO_FOLDER, filename)
        video_tracks = run_video_tracker(video_path)
        for t in video_tracks:
            t["video_file"] = filename
        all_tracks.extend(video_tracks)

    # Export CSV
    if all_tracks:
        with open(OUTPUT_CSV, "w", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=CSV_KEYS)
            writer.writeheader()
            writer.writerows(all_tracks)

    print(f"Exported {len(all_tracks)} tracked fish to {OUTPUT_CSV}")

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
# Function: run_video_tracker
# Description: Process a single video through both YOLO and DeepSort;
# return tracked fish data.
# Notes: N/A
def run_video_tracker(video_path):

    # Initialize new VideoData and DeepSort tracker for each video
    vidData = VideoData()
    vidData.v_filename = os.path.basename(video_path)
    tracker = DeepSortTracker()

    # Open video, determine FPS, and read first frame
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"Error: Could not open video: {video_path}")
        return []
    vidData.v_fps = cap.get(cv2.CAP_PROP_FPS) or FPS_DEFAULT
    ret, frame = cap.read()

    # Cycle through video frames until end of video
    while ret:

        # Initialize new FrameData object each frame
        frameData = FrameData(f_index=vidData.v_frame_index)
        frameData.f_detections = []

        # Determine most common class per-frame
        analyze_yolo_detections(frame, MODEL, frameData, vidData)

        # YOLO Post-Processing. TODO: Move this farther down. Currently doesn't produce accurate results because it's running too early.
        process_yolo_results(frameData, vidData, MODEL)

        # DeepSort Tracking
        deepsort_analysis(tracker, frame, frameData, vidData) 

        # Finalize disappeared tracks (detections that ended before the video ended)
        finalize_tracks(frameData, vidData, termination_reason="disappeared")

        # Increment frame index and read next frame
        vidData.v_frame_index += 1
        vidData.v_total_frames += 1
        ret, frame = cap.read()

    # Finalize forced tracks (detections that were still active at the end of the video)
    finalize_tracks(frameData, vidData, termination_reason="forced")

    cap.release()

    # Skip export if fish was not detected in video
    if not vidData.v_found_fish:
        no_fish_found(video_path, vidData.v_filename)
        return []

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

    return vidData.v_finished_tracks


# ****************************************************************
# Function: analyse_yolo_detections
# Description: Analyze YOLO detections to determine most common class of frame.
# Notes: Vars modified: found_fish (frame, video), detections(frame), frames with/without fish (video)
def analyze_yolo_detections(frame, model, frameData, vidData):

    # Run YOLO on frame
    results = model.predict(source=frame, verbose=False, stream=False, save=False)

    # Begin YOLO post-analysis
    if results:
        r = results[0]
        for box in r.boxes:
            x1, y1, x2, y2 = map(int, box.xyxy[0].cpu().numpy())
            conf_arr = box.conf.cpu().numpy()
            conf = float(conf_arr[0]) if conf_arr.size > 0 and conf_arr[0] is not None else 0.0
            cls_arr = box.cls.cpu().numpy()
            cls_id = int(cls_arr[0]) if cls_arr.size > 0 and cls_arr[0] is not None else -1
            if (x2 - x1)*(y2 - y1) < 100:
                continue

            # Mark if YOLO detected a fish in frame.
            try:
                cls_name = model.names[cls_id].lower()
            except Exception:
                cls_name = str(cls_id)
            if "fish" in cls_name:
                frameData.f_found_fish = True
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
    
    # Begin YOLO extrapolation for video-level stats
    if frameData.f_detections:

        # Most common detected class determined by-frame
        ids = [d[5] for d in frameData.f_detections]
        id_counter = Counter(ids)
        most_common_id = id_counter.most_common(1)[0][0]
        vidData.v_most_common_class = model.names[most_common_id]

        # Average confidence determined by freqneuency of most common object.
        vidData.v_avg_confidence_YL = [d[4] for d in frameData.f_detections if d[5] == most_common_id]
        if len(vidData.v_avg_confidence_YL) > 0:
            vidData.v_avg_confidence_YL = (sum(vidData.v_avg_confidence_YL) / len(vidData.v_avg_confidence_YL)) * 100
        else:
            vidData.v_avg_confidence_YL = 0.0
            
            
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

        if trackId not in vidData.v_active_tracks:
            vidData.v_active_tracks[trackId] = {
                "start_frame": frameData.f_index,
                "confidences": [],
                "directions": [],
                "best_conf": -1.0,
                "best_crop": None
            }

        vidData.v_active_tracks[trackId]["confidences"].append(obj["confidence"])
        vidData.v_active_tracks[trackId]["directions"].append(obj["direction"])

        x1, y1, x2, y2 = obj["bbox"]
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
            track_dict = build_track_summary(tid, track_data, frameData, vidData, None)
            if track_dict:
                vidData.v_finished_tracks.append(track_dict)

    elif termination_reason == "forced":
        for tid, track_data in vidData.v_active_tracks.items():
            track_dict = build_track_summary(tid, track_data, frameData, vidData, None)
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
    confidences = [c for c in track_data["confidences"] if c is not None]
    avg_conf_DS = sum(confidences) / len(confidences) if confidences else 0.0
    species_data = classify_image(image_path) if image_path else ("No image", 0.0)
    return {
        "trackId": trackId,
        "likely_class": vidData.v_most_common_class,
        "confidence": f"{vidData.v_avg_confidence_YL:.2f}%",
        "avg_confidence": f"{avg_conf_DS:.2f}%",
        "start_time_sec": track_data["start_frame"] / vidData.v_fps,
        "end_time_sec": frameData.f_index / vidData.v_fps,
        "direction": track_data["directions"][-1] if track_data["directions"] else "unknown",
        "best_crop": track_data.get("best_crop"),
        "species": species_data[0] if species_data else "No data",
        "species_confidence": f"{species_data[1]:.2f}%" if species_data else "No data"
    }


# ***************************************************************
# Function: no_fish_found
# Description: Moves video to no_fish if no fish detected. 
# Notes:
def no_fish_found(video_path, filename): # TODO: Change function name?
    print("***************************************************************")
    print(f"No fish detected in {filename}. Skipping export.")
    print("***************************************************************")
    # Copy video to no_fish folder
    no_fish_path = os.path.join("no_fish", filename)
    try:
        shutil.copy2(video_path, no_fish_path)
    except Exception as e:
        print(f"Error copying video to no_fish folder: {e}")


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
                cv2.imwrite(temp_image_path, enhanced_crop, [cv2.IMWRITE_JPEG_QUALITY, 95])
                
                # Classify the saved image
                species_data = classify_image(temp_image_path)
                species = species_data[0] if species_data else "No data"
                track["species"] = species
                track["species_confidence"] = f"{species_data[1]:.2f}%" if species_data and len(species_data) > 1 else "No data"
                
                # Create species subfolder and move image
                if species != "No data":
                    species_folder = os.path.join(FISH_IMAGE_DIR, species)
                    os.makedirs(species_folder, exist_ok=True)
                    final_image_path = os.path.join(species_folder, temp_image_name)
                    try:
                        shutil.move(temp_image_path, final_image_path)
                        track["image_path"] = final_image_path
                    except Exception as e:
                        print(f"Error moving image to species folder: {e}")
                        track["image_path"] = temp_image_path
                else:
                    track["image_path"] = temp_image_path
            else:
                track["image_path"] = None
            
            # Remove best_crop from track dict (no need to export it)
            track.pop("best_crop", None)


# ****************************************************************
# Function: classify_image
# Description: Loads, preprocesses, and classifies a single image file.
# Notes: Accepts image_path parameter for the image to classify.
def classify_image(image_path):
    # --- MODEL LOADING ---
    try:
        #print(f"Loading model from: {CLASSIFIER_MODEL_PATH}")
        model = load_model(CLASSIFIER_MODEL_PATH)
        print("Model loaded successfully.")
    except Exception as e:
        print(f"FATAL ERROR: Could not load the model file '{CLASSIFIER_MODEL_PATH}'.")
        print(f"Details: {e}")

    if model:
        try:
            # 1. Load the image and resize it
            img = load_img(image_path, target_size=IMAGE_SIZE)
            
            # 2. Convert to NumPy array and rescale
            img_array = img_to_array(img)
            img_array = img_array / 255.0 
            img_array = np.expand_dims(img_array, axis=0) # Add batch dimension

            # 3. Predict
            predictions = model.predict(img_array, verbose=0)
            
            # Get the highest prediction score and index
            pred_index = np.argmax(predictions)
            pred_label = CLASS_NAMES[pred_index]
            confidence = predictions[0][pred_index] * 100
            
            # 4. Return Results
            return pred_label, confidence
            
            # NOTE: We probably want to uncomment this but I haven't yet because then it would remove the good looking test image
            # 5. Clean up the file (Optional: uncomment if you want the file deleted after reading)
            # os.remove(CLASSIFIER_TARGET_FOLDER)
            # print(f"   -> File '{CROPPED_IMAGE_FILENAME}' deleted.")
            
        except FileNotFoundError:
            # This can happen if YOLO deletes the file just as the classifier tries to read it
            print(f"File not found at {image_path}. Skipping.")
        except Exception as e:
            print(f"ERROR during classification of {image_path}: {e}")
    else:
        print("Model was not found. Skipping classification.")

# ****************************************************************
# Function: get_image_name
# Description: Helper function for getting the image that deepsort
#   saved for the classifier.
# Notes: N/A
def get_image_name() -> str:
    p = Path(CLASSIFIER_TARGET_FOLDER)
    images = [f for f in p.iterdir() if f.is_file() and f.suffix.lower() in IMAGE_EXTS]
    if not images:
        raise FileNotFoundError(f'No image found in {CLASSIFIER_TARGET_FOLDER}')
    return images[0].name

main()