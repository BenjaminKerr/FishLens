import os
import csv
from ultralytics import YOLO
from tracking.deepsort_tracker import DeepSortTracker

VIDEO_FOLDER = "sample_data/"
OUTPUT_CSV = "fish_summary.csv"
FPS_DEFAULT = 30  # fallback if video FPS cannot be read

# YOLO model
model = YOLO("yolov8n.pt")

# DeepSort tracker
tracker = DeepSortTracker()

def run_video_tracker(video_path):
    import cv2
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"Error: Could not open video: {video_path}")
        return []

    video_fps = cap.get(cv2.CAP_PROP_FPS) or FPS_DEFAULT
    frame_index = 0
    active_tracks = {}
    finished_tracks = []

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
                detections.append([x1, y1, x2, y2, conf, cls_id])

        detections = tracker.filter_overlaps(detections, iou_thresh=0.5)
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

        # finalize disappeared tracks
        disappeared_ids = set(active_tracks.keys()) - current_track_ids
        for tid in disappeared_ids:
            track_data = active_tracks.pop(tid)
            confidences = [c for c in track_data["confidences"] if c is not None]
            avg_conf = sum(confidences)/len(confidences) if confidences else 0.0
            finished_tracks.append({
                "track_id": tid,
                "start_time_sec": track_data["start_frame"]/video_fps,
                "end_time_sec": frame_index/video_fps,
                "avg_confidence": avg_conf,
                "direction": track_data["directions"][-1] if track_data["directions"] else "unknown"
            })

        frame_index += 1

    # finalize remaining active tracks
    for tid, track_data in active_tracks.items():
        confidences = [c for c in track_data["confidences"] if c is not None]
        avg_conf = sum(confidences)/len(confidences) if confidences else 0.0
        finished_tracks.append({
            "track_id": tid,
            "start_time_sec": track_data["start_frame"]/video_fps,
            "end_time_sec": frame_index/video_fps,
            "avg_confidence": avg_conf,
            "direction": track_data["directions"][-1] if track_data["directions"] else "unknown"
        })

    cap.release()
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
    keys = ["video_file", "track_id", "start_time_sec", "end_time_sec", "avg_confidence", "direction"]
    with open(OUTPUT_CSV, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=keys)
        writer.writeheader()
        writer.writerows(all_tracks)

print(f"Exported {len(all_tracks)} tracked fish to {OUTPUT_CSV}")
