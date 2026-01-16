# ****************************************************************
# File: main.py
# Description: Main video processing script for YOLO and DeepSort.
# Notes: N/A
# ****************************************************************
import warnings
warnings.filterwarnings(
    "ignore",
    category=UserWarning,
    message="pkg_resources is deprecated"
)# Suppress specific warnings from pkg_resources
from fileinput import filename
import os
import csv
import cv2
import sys
from ultralytics import YOLO
from tracking.deepsort_tracker import DeepSortTracker
from collections import Counter
from pathlib import Path
import numpy as np
from keras.models import load_model
from keras.preprocessing.image import load_img, img_to_array

# Create and initialize video folder
model = YOLO("yolov8n.pt")
project_root = os.path.dirname(os.path.abspath(__file__))
VIDEO_FOLDER = sys.argv[1] if len(sys.argv) > 1 else os.path.join(project_root, "sample_data")
os.makedirs(VIDEO_FOLDER, exist_ok=True)
os.makedirs("no_fish", exist_ok=True)

# Classifier and DeepSORT target folder
# Also Classifier defaults
CLASSIFIER_TARGET_FOLDER = "images"
IMAGE_SIZE = (150, 150)
MODEL_PATH = os.path.join(project_root, "fish_classifier_model.h5")
CLASS_NAMES = ["Salmon", "Trout"] 
IMAGE_EXTS = {'.jpg', '.jpeg', '.png'}

# Output CSV filename and fallback FPS
OUTPUT_CSV = "fish_summary.csv"
FISH_IMAGE_DIR = "fish_images"
FPS_DEFAULT = 30 
MAX_EXPORT_PER_VIDEO = 1  # only export top fish per video

os.makedirs(FISH_IMAGE_DIR, exist_ok=True)

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
        keys = [
    "video_file",
    "track_id",
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

        with open(OUTPUT_CSV, "w", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=keys)
            writer.writeheader()
            writer.writerows(all_tracks)

    print(f"Exported {len(all_tracks)} tracked fish to {OUTPUT_CSV}")

# ****************************************************************
# Function: enhance_image
# Description: Enhance image quality through upscaling and sharpening.
# Notes: N/A
def enhance_image(crop):
    """Enhance image quality through upscaling and sharpening."""
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
# Description: Process a single video through both YOLO and DeepSort,
#     return tracked fish data.
# Notes: N/A
def run_video_tracker(video_path):

    import cv2

    frame_index = 0
    active_tracks = {}
    finished_tracks = []
    found_fish = False
    filename = os.path.basename(video_path)

    # create a fresh tracker for each video to avoid ID/history leakage
    tracker = DeepSortTracker()
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"Error: Could not open video: {video_path}")
        return []

    video_fps = cap.get(cv2.CAP_PROP_FPS) or FPS_DEFAULT

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        # Begin YOLO detection
        results = model.predict(source=frame, verbose=False, stream=False, save=False)
        detections = []

        # Begin YOLO analysis
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

                # Mark if YOLO detected a bird (fish) in any frame of this video
                try:
                    cls_name = model.names[cls_id].lower()
                except Exception:
                    cls_name = str(cls_id)
                if "bird" in cls_name:
                    found_fish = True
                detections.append([x1, y1, x2, y2, conf, cls_id])
            
        # Begin YOLO extrapolation for video-level stats
        if detections:

            # Most common detected class determined by-frame
            ids = [d[5] for d in detections]
            id_counter = Counter(ids)
            most_common_id = id_counter.most_common(1)[0][0]
            most_common_class = model.names[most_common_id]

            # Average confidence determined by freqneuency of most common object.
            confidence = [d[4] for d in detections if d[5] == most_common_id]
            if len(confidence) > 0:
                avg_confidence_YL = (sum(confidence) / len(confidence)) * 100
            else:
                avg_confidence_YL = 0.0

        # Begin DeepSort tracking

        # Use the tracker's default iou threshold (tuned in the tracker)
        detections = tracker.filter_overlaps(detections)
        tracked_objects = tracker.update(detections, frame)
        current_track_ids = set()

        for obj in tracked_objects:
            track_id = obj["track_id"]
            current_track_ids.add(track_id)

            if track_id not in active_tracks:
                active_tracks[track_id] = {
                    "start_frame": frame_index,
                    "confidences": [],
                    "directions": [],
                    "best_conf": -1.0,
                    "best_crop": None
                }

            active_tracks[track_id]["confidences"].append(obj["confidence"])
            active_tracks[track_id]["directions"].append(obj["direction"])

            # ---------------------------
            # Capture best image crop
            # ---------------------------

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
                if conf > active_tracks[track_id]["best_conf"]:
                    active_tracks[track_id]["best_conf"] = conf
                    active_tracks[track_id]["best_crop"] = crop.copy()

        # Finalize disappeared tracks - only export if track lasted long enough
        disappeared_ids = set(active_tracks.keys()) - current_track_ids 
        for tid in disappeared_ids:
            track_data = active_tracks.pop(tid)
            track_dict = finalize_track(tid, track_data, frame_index, video_fps, most_common_class, avg_confidence_YL)
            if track_dict:
                finished_tracks.append(track_dict)
        frame_index += 1

    # Finalize remaining active tracks
    for tid, track_data in active_tracks.items():
        track_dict = finalize_track(tid, track_data, frame_index, video_fps, most_common_class, avg_confidence_YL)
        if track_dict:
            finished_tracks.append(track_dict)

    cap.release()

    # Skip export if fish was not detected in video
    if not found_fish:
        print("***************************************************************")
        print(f"No fish detected in {filename}. Skipping export.")
        print("***************************************************************")
        return []
    
        # ------------------------------------------------------
    # Select top tracks and export best image
    # ------------------------------------------------------

    if MAX_EXPORT_PER_VIDEO and len(finished_tracks) > MAX_EXPORT_PER_VIDEO:
        finished_tracks.sort(
            key=lambda x: float(x["avg_confidence"].replace("%", "")),
            reverse=True
        )
        finished_tracks = finished_tracks[:MAX_EXPORT_PER_VIDEO]

    for track in finished_tracks:
        best_crop = next(
            (t["best_crop"] for t in active_tracks.values() if t.get("best_crop") is not None),
            None
        )

        if best_crop is not None:
            enhanced_crop = enhance_image(best_crop)
            image_name = f"{os.path.splitext(filename)[0]}_track_{track['track_id']}.jpg"
            image_path = os.path.join(FISH_IMAGE_DIR, image_name)
            cv2.imwrite(image_path, enhanced_crop, [cv2.IMWRITE_JPEG_QUALITY, 95])
            track["image_path"] = image_path
        else:
            track["image_path"] = None

    return finished_tracks

# ****************************************************************
# Function: finalize_track
# Description: Helper function for finalizing track data.
# Notes: N/A
def finalize_track(track_id, track_data, frame_index, video_fps, most_common_class, avg_confidence_YL):
    duration_sec = (frame_index - track_data["start_frame"]) / video_fps
    if duration_sec < 1.0:
        return None
    confidences = [c for c in track_data["confidences"] if c is not None]
    avg_conf_DS = sum(confidences) / len(confidences) if confidences else 0.0
    species_data = classify_image()
    return {
        "track_id": track_id,
        "likely_class": most_common_class,
        "confidence": f"{avg_confidence_YL:.2f}%",
        "avg_confidence": f"{avg_conf_DS:.2f}%",
        "start_time_sec": track_data["start_frame"] / video_fps,
        "end_time_sec": frame_index / video_fps,
        "direction": track_data["directions"][-1] if track_data["directions"] else "unknown",
        "species": species_data[0] if species_data else "No data",
        "species_confidence": f"{species_data[1]:.2f}%" if species_data else "No data"
    }

# ****************************************************************
# Function: classify_image
# Description: Loads, preprocesses, and classifies a single image file.
# Notes: Lots of comments after the return that could be uncommented
#   and moved up to delete the image after classification. Currently is
#   commented to keep the test image for use.
def classify_image():
    # --- MODEL LOADING ---
    try:
        print(f"Loading model from: {MODEL_PATH}")
        model = load_model(MODEL_PATH)
        print("Model loaded successfully.")
    except Exception as e:
        print(f"FATAL ERROR: Could not load the model file '{MODEL_PATH}'.")
        print(f"Details: {e}")

    if model:
        try:
            # 1. Load the image and resize it
            img = load_img(CLASSIFIER_TARGET_FOLDER, target_size=IMAGE_SIZE)
            
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
            print(f"File not found at {CLASSIFIER_TARGET_FOLDER}. Skipping.")
        except Exception as e:
            print(f"ERROR during classification of {CLASSIFIER_TARGET_FOLDER}: {e}")
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