import os
import csv
from ultralytics import YOLO
from tracking.deepsort_tracker import DeepSortTracker
from collections import Counter

VIDEO_FOLDER = "sample_data/"
OUTPUT_CSV = "fish_summary.csv"
FPS_DEFAULT = 30  # fallback if video FPS cannot be read

# YOLO model
model = YOLO("yolov8n.pt")

# NOTE: create a tracker per-video inside run_video_tracker to avoid
# carrying tracker state (track IDs, histories) across multiple files.

def run_video_tracker(video_path):
    import cv2
    # create a fresh tracker for each video to avoid ID/history leakage
    tracker = DeepSortTracker()
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"Error: Could not open video: {video_path}")
        return []

    video_fps = cap.get(cv2.CAP_PROP_FPS) or FPS_DEFAULT
    frame_index = 0
    active_tracks = {}
    finished_tracks = []
    found_fish = False

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        # YOLO detection
        results = model.predict(source=frame, verbose=False, stream=False, save=False)
        detections = []

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
                # mark if YOLO detected a bird in any frame of this video
                try:
                    cls_name = model.names[cls_id].lower()
                except Exception:
                    cls_name = str(cls_id)
                if "bird" in cls_name:
                    found_fish = True
                detections.append([x1, y1, x2, y2, conf, cls_id])
            
        if detections:
            # Frames with detections are analyzed to determine what 
            # object the video most likely contains.
            # Note: This assumes the video contains only one type of object.
            ids = [d[5] for d in detections]
            id_counter = Counter(ids)
            most_common_id = id_counter.most_common(1)[0][0]
            most_common_class = model.names[most_common_id]

            # Average confidence is determined based on how often the most
            # common object is detected.
            confidence = [d[4] for d in detections if d[5] == most_common_id]

            if len(confidence) > 0:
                avg_confidence = (sum(confidence) / len(confidence)) * 100
            else:
                avg_confidence = 0.0

        # use the tracker's default iou threshold (tuned in the tracker)
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
                    "directions": []
                }

            active_tracks[track_id]["confidences"].append(obj["confidence"])
            active_tracks[track_id]["directions"].append(obj["direction"])

        # finalize disappeared tracks - only export if track lasted long enough
        disappeared_ids = set(active_tracks.keys()) - current_track_ids
        for tid in disappeared_ids:
            track_data = active_tracks.pop(tid)
            
            # Only export if track lasted at least 0.5 seconds (helps filter noise)
            duration_sec = (frame_index - track_data["start_frame"]) / video_fps
            if duration_sec < 1.0:
                continue

            det_hist = tracker.detection_history.get(tid, [])
            confs = [d["confidence"] for d in det_hist if d["confidence"] is not None]
            avg_conf = sum(confs) / len(confs) if confs else 0.0

            finished_tracks.append({
                "video_file": filename,
                "track_id": tid,
                "likely_class": most_common_class,
                "confidence": f"{avg_confidence:.2f}%",
                "start_time_sec": track_data["start_frame"] / video_fps,
                "end_time_sec": frame_index / video_fps,
                "avg_confidence": avg_conf,
                "direction": tracker.previous_directions.get(tid, "unknown")
            })

        frame_index += 1

    # finalize remaining active tracks
    for tid, track_data in active_tracks.items():
        # Only export if track lasted long enough
        duration_sec = (frame_index - track_data["start_frame"]) / video_fps
        if duration_sec < 1.0:
            continue
            
        confidences = [c for c in track_data["confidences"] if c is not None]
        avg_conf = sum(confidences)/len(confidences) if confidences else 0.0
        finished_tracks.append({
            "video_file": filename,
            "track_id": tid,
            "likely_class": most_common_class,
            "confidence": f"{avg_confidence:.2f}%",
            "start_time_sec": track_data["start_frame"] / video_fps,
            "end_time_sec": frame_index / video_fps,
            "avg_confidence": avg_conf,
            "direction": track_data["directions"][-1] if track_data["directions"] else "unknown"
        })

    cap.release()
    # If YOLO never detected a bird in this video, don't export any tracks
    if not found_fish:
        print(f"No fish detected in {video_path} — skipping export.")
        return []

    return finished_tracks


# Process all videos
all_tracks = []
for filename in os.listdir(VIDEO_FOLDER):
    video_path = os.path.join(VIDEO_FOLDER, filename)
    video_tracks = run_video_tracker(video_path)
    for t in video_tracks:
        t["video_file"] = filename
    all_tracks.extend(video_tracks)

# Export CSV
if all_tracks:
    keys = ["video_file", "track_id", "likely_class", "confidence", "start_time_sec", "end_time_sec", "avg_confidence", "direction"]
    with open(OUTPUT_CSV, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=keys)
        writer.writeheader()
        writer.writerows(all_tracks)

print(f"Exported {len(all_tracks)} tracked fish to {OUTPUT_CSV}")