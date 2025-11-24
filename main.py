import cv2
import os
import numpy as np
from YOLO.detector import YoloDetector
from tracking.deepsort_tracker import DeepSortTracker
from YOLO.yolo import analyze_videos

print("MAIN.PY IS RUNNING...")

# Initialize YOLO and DeepSort
detector = YoloDetector(weights_path="YOLO/yolov8n.pt")
tracker = DeepSortTracker()
track_history = {}  # To store centroids for direction tracking

#currently taking moving up as upstream and down (y axis) as downstream
def get_direction(track_id, centroid):
    if track_id not in track_history:
        track_history[track_id] = [centroid]
        return None

    track_history[track_id].append(centroid)

    # Use total movement over last N frames
    N = min(5, len(track_history[track_id]))
    y_start = track_history[track_id][-N][1]
    y_end = centroid[1]

    if y_end < y_start - 2:  # add a small threshold to avoid jitter
        return "upstream"
    elif y_end > y_start + 2:
        return "downstream"
    else:
        return "stationary"

# def get_direction(track_id, centroid):
#     """Determine upstream/downstream direction based on previous centroid positions."""
#     if track_id not in track_history:
#         track_history[track_id] = [centroid]
#         return None
#     prev_y = track_history[track_id][-1][1]
#     track_history[track_id].append(centroid)
#     if centroid[1] < prev_y:
#         return "upstream"
#     elif centroid[1] > prev_y:
#         return "downstream"
#     else:
#         return "stationary"

def run_video_tracker(video_path):
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print("Error: Could not open video.")
        return

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        h, w, _ = frame.shape

        # 1. Detect fish using YOLO
        detections = detector.detect(frame)  # [x1, y1, x2, y2, conf, class_id]

        # 2. Clamp bounding boxes and prepare for DeepSort
        ds_dets = []
        for det in detections:
            x1, y1, x2, y2, conf, cls_id = det
            x1 = max(0, min(x1, w-1))
            x2 = max(0, min(x2, w-1))
            y1 = max(0, min(y1, h-1))
            y2 = max(0, min(y2, h-1))
            ds_dets.append([x1, y1, x2, y2, conf, int(cls_id)])

        # 3. Update tracker only if detections exist
        if len(ds_dets) > 0:
            tracks = tracker.update(ds_dets, frame=frame)  # pass actual frame for embeddings
        else:
            tracks = []

        # 4. Draw tracks and directions
        for track in tracks:
            track_id = track["track_id"]
            x1, y1, x2, y2 = map(int, track["bbox"])
            centroid = ((x1 + x2) // 2, (y1 + y2) // 2)
            direction = get_direction(track_id, centroid)

            # Draw bounding box and label
            cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 255, 0), 2)
            label = f"ID:{track_id}"
            if direction:
                label += f" {direction}"
            cv2.putText(frame, label, (x1, y1 - 10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 2)

        # 5. Display video
        cv2.imshow("Fish Tracker", frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    cap.release()
    cv2.destroyAllWindows()

if __name__ == "__main__":
    analyze_videos()
    for filename in os.listdir("results/has_fish/"):
        video_path = os.path.join("results/has_fish/", filename)
        run_video_tracker(video_path)
