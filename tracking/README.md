# Tracking Module (DeepSORT)

This directory contains all DeepSORT tracking code.

DeepSORT will take YOLO detections as input and output track IDs for each fish.
Direction detection and species classification will be added later.

Pipeline:
YOLO → format detections → DeepSORT → track IDs → direction → fishspeciesclassifier
