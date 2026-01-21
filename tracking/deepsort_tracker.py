import numpy as np
from deep_sort_realtime.deepsort_tracker import DeepSort
import torch
from torchvision.ops import nms

class DeepSortTracker:
    # ****************************************************************
    # Function: __init__
    # Description: Initialize the DeepSort tracker with configuration parameters
    #     and tracking state containers.
    # Notes: N/A
    def __init__(self):
        self.tracker = DeepSort(
            max_age=50,          # tracks persist 50 frames after last detection before dying
            n_init=10,            # require only 3 detections to confirm track (reduce fragmentation)
            max_iou_distance=0.6,  # stricter spatial matching - prevents distant objects from merging
            max_cosine_distance=0.4  # looser appearance matching - allows same object to have appearance variations
        )

        # track_id → list of centroid tuples
        self.track_positions = {}
        # track_id → last known direction
        self.previous_directions = {}
        # track_id → list of detection dictionaries
        self.detection_history = {}

    # ****************************************************************
    # Function: filter_overlaps
    # Description: Remove overlapping boxes using NMS, keeping the highest
    #     confidence detection for each region.
    # Notes: N/A
    def filter_overlaps(self, detections, iou_thresh=0.7):
        """
    Remove overlapping boxes, keeping the highest confidence.
    detections = [[x1, y1, x2, y2, conf, cls_id], ...]
        """
        if not detections:
            return []

        # Ensure all detections are valid lists/tuples with at least 5 elements
        detections = [d for d in detections if isinstance(d, (list, tuple)) and len(d) >= 5]

        if not detections:
            return []

        boxes = torch.tensor([d[:4] for d in detections], dtype=torch.float32)
        scores = torch.tensor([d[4] for d in detections], dtype=torch.float32)

        keep_idxs = nms(boxes, scores, iou_thresh)
        return [detections[i] for i in keep_idxs]

    # ****************************************************************
    # Function: _get_direction
    # Description: Compute horizontal movement direction of a tracked object
    #     by comparing first and last known positions.
    # Notes: N/A
    def _get_direction(self, track_id, centroid):
        """
        Compute horizontal direction using first and last position
        Returns 'upstream' (left) or 'downstream' (right)
        """
        positions = self.track_positions.get(track_id, [])
        positions.append(centroid)
        self.track_positions[track_id] = positions

        if len(positions) < 2:
            return "unknown"

        dx = positions[-1][0] - positions[0][0]  # horizontal movement
        if abs(dx) < 5:  # minimal movement threshold
            return "stationary"

        direction = "upstream" if dx < 0 else "downstream"
        self.previous_directions[track_id] = direction
        return direction

    # ****************************************************************
    # Function: update
    # Description: Update DeepSort tracker with new detections from a frame
    #     and return confirmed tracked objects with metadata.
    # Notes: N/A
    def update(self, detections, frame):
        """
        Update DeepSort with new detections.
        detections = [x1, y1, x2, y2, conf, cls]
        """
        # Filter tiny boxes and low-confidence detections
        detections = [d for d in detections if (d[2]-d[0])*(d[3]-d[1]) > 100 and d[4] > 0.5]

        formatted = [
            ([x1, y1, x2, y2], conf, cls)
            for x1, y1, x2, y2, conf, cls in detections
        ]

        tracks = self.tracker.update_tracks(formatted, frame=frame)
        results = []

        for t in tracks:
            if not t.is_confirmed():
                continue

            track_id = t.track_id
            x1, y1, x2, y2 = map(int, t.to_ltrb())
            centroid = ((x1 + x2) // 2, (y1 + y2) // 2)

            direction = self._get_direction(track_id, centroid)

            # Log detections
            if track_id not in self.detection_history:
                self.detection_history[track_id] = []

            det_conf = t.get_det_conf()
            det_conf = float(det_conf) if det_conf is not None else 0.0

            self.detection_history[track_id].append({
                "confidence": det_conf,
                "class_id": t.det_class,
                "centroid": centroid
            })

            results.append({
                "track_id": track_id,
                "bbox": (x1, y1, x2, y2),
                "centroid": centroid,
                "direction": direction,
                "confidence": det_conf,
                "class_id": t.det_class
            })

        return results

    # ****************************************************************
    # Function: get_track_summaries
    # Description: Generate summaries of confirmed tracks that meet minimum
    #     frame duration threshold for export.
    # Notes: N/A
    def get_track_summaries(self, min_frames=3):
        """
        Summarize confirmed tracks with minimal frames.
        Only tracks lasting >= min_frames will be exported.
        """
        summaries = []
        min_frames = 10
        for track_id, centroids in self.track_positions.items():
            if len(centroids) < min_frames:
                continue  # ignore short tracks

            detection_list = self.detection_history.get(track_id, [])
            confidences = [d["confidence"] for d in detection_list if d["confidence"] is not None]
            avg_conf = float(np.mean(confidences)) if confidences else 0.0
            class_id = detection_list[0]["class_id"] if detection_list else None
            direction = self.previous_directions.get(track_id, "unknown")

            summaries.append({
                "track_id": track_id,
                "direction": direction,
                "avg_confidence": avg_conf,
                "detections_count": len(detection_list),
                "class_id": class_id
            })

        return summaries
