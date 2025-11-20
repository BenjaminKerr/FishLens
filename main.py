import cv2
from tracking.deepsort_tracker import DeepSortTracker
import numpy as np
print("MAIN.PY IS RUNNING...")

# Store history of centroids per track ID
track_history = {}

def get_direction(track_id, centroid):
    """
    Determines upstream or downstream direction
    based on horizontal centroid movement (x-axis).
    """
    if track_id not in track_history:
        track_history[track_id] = []
        return "Unknown"

    track_history[track_id].append(centroid)

    # Need at least 2 points to determine movement
    if len(track_history[track_id]) < 2:
        return "Unknown"

    x_prev = track_history[track_id][-2][0]
    x_curr = track_history[track_id][-1][0]

    # Movement along X axis
    if x_curr > x_prev:
        return "Downstream"     # moving right
    elif x_curr < x_prev:
        return "Upstream"       # moving left
    else:
        return "Stationary"

# ------------------------
# MAIN VIDEO PIPELINE
# ------------------------
def run_video_tracker(video_path):
    cap = cv2.VideoCapture(video_path)
    tracker = DeepSortTracker()

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        # Example placeholder detection
        h, w = frame.shape[:2]
        detections = [
            [w//4, h//3, w//4 + 120, h//3 + 60, 0.9, 0]
        ]

        results = tracker.update(detections)

        for r in results:
            x1, y1, x2, y2 = map(int, r["bbox"])
            track_id = r["track_id"]

            # Calculate centroid
            cx = int((x1 + x2) / 2)
            cy = int((y1 + y2) / 2)
            centroid = (cx, cy)

            direction = get_direction(track_id, centroid)

            # Draw results
            cv2.rectangle(frame, (x1, y1), (x2, y2), (0,255,0), 2)
            cv2.circle(frame, (cx, cy), 4, (0,255,255), -1)
            cv2.putText(
                frame,
                f"ID:{track_id} {direction}",
                (x1, y1 - 10),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                (0,255,0),
                2
            )

        cv2.imshow("Tracking + Direction (X-axis)", frame)
        if cv2.waitKey(20) & 0xFF == ord("q"):
            break

    cap.release()
    cv2.destroyAllWindows()

run_video_tracker("videos/samples_data_vtest.avi")