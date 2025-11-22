from deep_sort_realtime.deepsort_tracker import DeepSort
import numpy as np

class DeepSortTracker:
    def __init__(self):
        # Use default embedder
        self.tracker = DeepSort(
            max_age=50, #if fish not detected for 30 frames, delete track
            n_init=5, #number of frames to confirm a track
            max_iou_distance=0.7, #how similar bboxes need to be to be considered the same object
            max_cosine_distance=0.2 #embedding distance threshold
        )

    def update(self, detections, frame=None):
        """
        detections: list of [x1, y1, x2, y2, confidence, class_id]
        frame: the actual video frame for embedding (required)
        """
        if frame is None:
            raise ValueError("A real video frame must be provided for DeepSort embeddings.")

        formatted = []
        for det in detections:
            x1, y1, x2, y2, conf, class_id = det
            formatted.append(([x1, y1, x2, y2], conf, class_id))

        # Use the actual frame
        tracks = self.tracker.update_tracks(formatted, frame=frame)

        results = []
        for track in tracks:
            if not track.is_confirmed():
                continue
            results.append({
                "track_id": track.track_id,
                "bbox": track.to_ltrb(),
                "class_id": track.get_det_class(),
                "confidence": track.get_det_conf()
            })
        return results
