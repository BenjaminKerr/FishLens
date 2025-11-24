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

# Parameters
HISTORY_LENGTH = 5        # number of previous positions to smooth direction
DIRECTION_THRESHOLD = 5   # minimum pixel movement to count as direction change


def run_video_tracker(video_path):
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"Error: Could not open video: {video_path}")
        return

    print(f"Tracking video: {video_path}")

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        h, w, _ = frame.shape

        # 1. ---- YOLO DETECTION ----
        detections = detector.detect(frame)  
        # detections = [x1, y1, x2, y2, conf, cls]

        # clamp and ensure proper format
        cleaned_dets = []
        for det in detections:
            x1, y1, x2, y2, conf, cls_id = det

            x1 = max(0, min(int(x1), w - 1))
            x2 = max(0, min(int(x2), w - 1))
            y1 = max(0, min(int(y1), h - 1))
            y2 = max(0, min(int(y2), h - 1))

            # Optional: filter tiny boxes to reduce duplicates
            if (x2 - x1) * (y2 - y1) < 100:  # adjust 100 to your min fish size
                continue

            cleaned_dets.append([x1, y1, x2, y2, float(conf), int(cls_id)])

        # 2. ---- UPDATE DEEPSORT ----
        tracks = tracker.update(cleaned_dets, frame)

        # 3. ---- DRAW TRACKS ----
        for track in tracks:
            track_id = track["track_id"]
            x1, y1, x2, y2 = track["bbox"]

            # --- update track position history ---
            x_center = int((x1 + x2) / 2)
            y_center = int((y1 + y2) / 2)
            if "positions" not in track:
                track["positions"] = []
                track["previous_direction"] = "downstream"  # default

            track["positions"].append((x_center, y_center))
            if len(track["positions"]) > HISTORY_LENGTH:
                track["positions"].pop(0)

            # --- calculate smoothed direction ---
            direction = track["direction"] 

            # Draw bounding box
            cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 255, 0), 2)

            # Label
            label = f"ID:{track_id} {direction}"
            cv2.putText(frame, label, (x1, y1 - 10),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.5, (0, 255, 0), 2)

        # 4. ---- SHOW FRAME ----
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
