# ******************************
# File: DeepSortTracker.py
# Description: DeepSort-based tracking system for analyzing video frames.
#              Processes detections from YOLO model, filters them, and maintains 
#              track histories to determine movement direction and confidence.
# Author: Aleksen Thayer
# Contributers: None
# Notes: Last edited 2/11/2026
# ******************************

from typing import List, Dict, Tuple, Optional, Any
import os
import numpy as np
from deep_sort_realtime.deepsort_tracker import DeepSort
import torch
from torchvision.ops import nms

class DeepSortTracker:
    MIN_BOX_AREA = 100
    MIN_CONFIDENCE = 0.5
    MIN_MOVE_THRESHOLD = 5
    MAX_TRACK_HISTORY = 50
    MIN_FRAMES_FOR_SUMMARY = 3
    DEFAULT_NMS_IOU_THRESHOLD = 0.85
    MIN_POSITIONS_FOR_DIRECTION = 2
    DIRECTION_HISTORY_WINDOW = 12
    # A single fish should never occupy more than ~65% of the frame height
    # while also being nearly square. A diagonally-swimming fish will produce
    # a taller axis-aligned box than a horizontal fish, but it will still be
    # wider than it is tall (fish are long animals). Two vertically stacked
    # fish collapse into a box that is both tall (>65% frame height) AND
    # squarish (width/height < 2.5). Requiring both conditions avoids
    # discarding legitimate diagonal-swimming detections.
    MAX_BOX_HEIGHT_FRACTION = 0.65
    MAX_BOX_MERGED_ASPECT_RATIO = 2.5  # width/height; below this = suspiciously square
    # Lowered from 10→3: fish pass through quickly; waiting 10 confirmed frames
    # before track activation causes many short-duration fish to be missed entirely.
    DEEPSORT_N_INIT = 3
    # Keep tracks alive through brief detection dropouts so one fish is less
    # likely to be split into multiple exported fragments.
    DEEPSORT_MAX_AGE = 60
    
    # ******************************
    # Function: __init__
    # Description: Initialize the DeepSort tracker with configuration parameters
    #              and tracking state containers.

    def __init__(self) -> None:
        n_init = max(1, int(os.getenv("FISHLENS_DEEPSORT_N_INIT", str(self.DEEPSORT_N_INIT))))
        max_age = max(1, int(os.getenv("FISHLENS_DEEPSORT_MAX_AGE", str(self.DEEPSORT_MAX_AGE))))
        self.tracker = DeepSort(
            max_age=max_age,
            n_init=n_init,
            max_iou_distance=0.7,
            max_cosine_distance=0.4
        )

        # trackId: list of (x, y) centroids for direction analysis
        # Example: {1: [(100, 200), (105, 205)], 2: [(300, 400)]}
        self.trackPositions: Dict[int, List[Tuple[int, int]]] = {}
        
        # trackId: last known direction
        # Example: {1: "downstream", 2: "upstream"}
        self.previousDirections: Dict[int, str] = {}
        
        # trackId: list of detection metadata for history and summarization
        # Example: {1: [{"confidence": 0.9, "classId": 0}]}
        self.detectionHistory: Dict[int, List[Dict[str, Any]]] = {}

    # ******************************
    # Function: filterOverlaps
    # Description: Remove overlapping boxes using NMS, keeping the highest
    #              confidence detection for each region.

    def filterOverlaps(
        self, 
        detections: List[Tuple], 
        iouThresh=DEFAULT_NMS_IOU_THRESHOLD
    ) -> List[Tuple]:
        
        # Ensure all detections are valid lists/tuples with at least 5 elements
        detections = [
            d for d in (detections or []) 
            if isinstance(d, (list, tuple)) and len(d) >= 5
        ]

        if not detections:
            return []

        # Extract bounding boxes and convert to tensor format for NMS
        boxes = torch.tensor([d[:4] for d in detections], dtype=torch.float32)
        
        # Extract confidence scores (fifth element) and convert to tensor
        scores = torch.tensor([d[4] for d in detections], dtype=torch.float32)

        # Keep boxes that don't have too much overlap
        keepIdxs = nms(boxes, scores, iouThresh)
        return [detections[i] for i in keepIdxs]

    # ******************************
    # Function: getDirection
    # Description: Compute horizontal movement direction of a tracked object
    #              by comparing first and last known positions.

    def getDirection(self, trackId: int, centroid: Tuple[int, int]) -> str:
        # Store centroid history list for this track
        positions = self.trackPositions.get(trackId, [])
        positions.append(centroid)
        if len(positions) > self.DIRECTION_HISTORY_WINDOW:
            positions = positions[-self.DIRECTION_HISTORY_WINDOW:]
        self.trackPositions[trackId] = positions

        # Need at least 2 positions to determine direction
        if len(positions) < self.MIN_POSITIONS_FOR_DIRECTION:
            return "unknown"

        # Measure horizontal displacement across a recent window instead of the entire
        # lifetime of the track. This is more stable for fragmented tracks and real
        # turnarounds, where "first point to current point" can become misleading.
        dx = positions[-1][0] - positions[0][0]

        # If object moved less than threshold, considered stationary
        if abs(dx) < self.MIN_MOVE_THRESHOLD:
            return "stationary"

        direction = "upstream" if dx < 0 else "downstream"
        self.previousDirections[trackId] = direction
        return direction

    # ******************************
    # Function: update
    # Description: Update DeepSort tracker with new detections from a frame
    #              and return confirmed tracked objects with metadata.

    def update(
        self, 
        detections: List[Tuple], 
        frame: np.ndarray
    ) -> List[Dict[str, Any]]:
        
        h, w = frame.shape[:2]
        max_box_h = h * self.MAX_BOX_HEIGHT_FRACTION

        # Filter tiny boxes, low-confidence detections, and oversized merged boxes.
        # A merged double-fish box is both tall (>65% frame height) AND squarish
        # (width/height < 2.5). A diagonal single fish may be tall but will still
        # be clearly wider than it is tall, so it passes the aspect ratio check.
        def _is_merged(d):
            bh = d[3] - d[1]
            bw = d[2] - d[0]
            if bh <= max_box_h:
                return False
            aspect = bw / max(bh, 1)
            return aspect < self.MAX_BOX_MERGED_ASPECT_RATIO

        detections = [
            d for d in detections
            if (d[2] - d[0]) * (d[3] - d[1]) > self.MIN_BOX_AREA
            and d[4] > self.MIN_CONFIDENCE
            and not _is_merged(d)
        ]

        # Convert to DeepSORT format (bbox, conf, cls).
        # deep_sort_realtime expects [left, top, width, height].
        formatted = [
            ([x1, y1, x2 - x1, y2 - y1], conf, cls)
            for x1, y1, x2, y2, conf, cls in detections
        ]

        # Let DeepSort process the detections
        tracks = self.tracker.update_tracks(formatted, frame=frame)
        results = []

        for t in tracks:
            if t.is_confirmed() and getattr(t, "time_since_update", 1) == 0:
                trackId = t.track_id
                bbox = t.to_ltrb(orig=True)
                if bbox is None:
                    bbox = t.to_ltrb()
                x1, y1, x2, y2 = map(int, bbox)
                centroid = ((x1 + x2) // 2, (y1 + y2) // 2)

                direction = self.getDirection(trackId, centroid)

                # Initialize history for new tracks
                if trackId not in self.detectionHistory:
                    self.detectionHistory[trackId] = []

                # Get detection confidence, handle None case
                detConf = t.get_det_conf()
                detConf = float(detConf) if detConf is not None else 0.0

                # Append detection info to history
                self.detectionHistory[trackId].append(
                    {
                        "confidence": detConf,
                        "classId": t.det_class,
                        "centroid": centroid
                    }
                )
                
                # Package all track info into a dict
                results.append(
                    {
                        "trackId": trackId,
                        "bbox": (x1, y1, x2, y2),
                        "centroid": centroid,
                        "direction": direction,
                        "confidence": detConf,
                        "classId": t.det_class
                    }
                )

        return results

    # ******************************
    # Function: getTrackSummaries
    # Description: Generate summaries of confirmed tracks that meet minimum
    #              frame duration threshold for export.

    def getTrackSummaries(self, minFrames=10) -> List[Dict[str, Any]]:
        summaries = []

        # Loop through all tracks we've seen
        for trackId, centroids in self.trackPositions.items():
            
            # Skip short tracks to filter out false positives
            if len(centroids) >= minFrames:
                detectionList = self.detectionHistory.get(trackId, [])
                
                # Extract all confidence scores, ignoring None values
                confidences = [
                    d["confidence"] for d in detectionList 
                    if d["confidence"] is not None
                ]
                
                # Calculate average confidence across all detections
                avgConf = float(np.mean(confidences)) if confidences else 0.0
                
                # Get class ID from first detection
                classId = detectionList[0]["classId"] if detectionList else None
                
                # Get final determined direction for this track
                direction = self.previousDirections.get(trackId, "unknown")

                # Create summary record for this track
                summaries.append(
                    {
                        "trackId": trackId,
                        "direction": direction,
                        "avgConfidence": avgConf,
                        "detectionCount": len(detectionList),
                        "classId": classId
                    }
                )
                
        return summaries
