import cv2
from tracking.deepsort_tracker import DeepSortTracker

# Load image
frame = cv2.imread("images/sample_image.jpg")

# Dummy YOLO detections for testing
# Format: x1, y1, x2, y2, confidence, class_id
detections = [
    [50, 50, 200, 200, 0.9, 0]
]

# Initialize tracker
tracker = DeepSortTracker()

# Update tracker with frame
results = tracker.update(detections)

# Show results
for r in results:
    x1, y1, x2, y2 = map(int, r["bbox"])
    track_id = r["track_id"]
    cv2.rectangle(frame, (x1, y1), (x2, y2), (0,255,0), 2)
    cv2.putText(frame, f"ID:{track_id}", (x1, y1-10),
                cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0,255,0), 2)


cv2.imshow("Tracked Image", frame)
cv2.waitKey(0)
cv2.destroyAllWindows()
