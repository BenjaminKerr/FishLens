import numpy as np
from deep_sort_realtime.deepsort_tracker import DeepSort

class DeepSortTracker:
    def __init__(self):
        self.tracker = DeepSort(
            max_age=50,
            n_init=5,
            max_iou_distance=0.7,
            max_cosine_distance=0.2
        )

        # Track history: track_id → list of centroids
        self.track_positions = {}
        self.previous_directions = {}

    def _get_direction(self, track_id, centroid, history=5, threshold=5):
        """
        Calculate direction of a track using its recent centroids.
        Returns 'upstream' or 'downstream'.
        """
        # Get or initialize positions history
        positions = self.track_positions.get(track_id, [])
        positions.append(centroid)

        # Keep only last N positions
        if len(positions) > history:
            positions = positions[-history:]
        self.track_positions[track_id] = positions

        # Not enough data yet
        if len(positions) < 2:
            return self.previous_directions.get(track_id, "downstream")

        dy = positions[-1][1] - positions[0][1]
        if abs(dy) < threshold:
            return self.previous_directions.get(track_id, "downstream")

        direction = "upstream" if dy < 0 else "downstream"
        self.previous_directions[track_id] = direction
        return direction

    def update(self, detections, frame):
        """
        Update DeepSort tracks with new detections.
        detections: list of [x1, y1, x2, y2, conf, cls]
        """
        # Format detections for DeepSort
        formatted = [([x1, y1, x2, y2], conf, cls) for x1, y1, x2, y2, conf, cls in detections]

        tracks = self.tracker.update_tracks(formatted, frame=frame)
        results = []

        for t in tracks:
            if not t.is_confirmed():
                continue

            # Bounding box
            x1, y1, x2, y2 = map(int, t.to_ltrb())

            # Centroid
            centroid = ((x1 + x2) // 2, (y1 + y2) // 2)

            # Calculate smoothed direction
            direction = self._get_direction(t.track_id, centroid)

            results.append({
                "track_id": t.track_id,
                "bbox": (x1, y1, x2, y2),
                "centroid": centroid,
                "direction": direction,
                "class_id": t.det_class,
                "confidence": t.det_conf
            })

        return results
