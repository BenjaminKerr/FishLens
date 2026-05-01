from deepsort_tracker import DeepSortTracker

# Fake YOLO detections:
# x1, y1, x2, y2, confidence, class_id
detections = [
    [100, 150, 200, 300, 0.90, 0],
    [105, 155, 205, 305, 0.85, 0]
]

tracker = DeepSortTracker()

results = tracker.update(detections)

print("TRACKER OUTPUT:")
print(results)
