#------------------------------------------------------------------
# Name: YOLOv8 Image Analysis Script
# Summary and Notes:
# This script analyses videos to detect the probability of their
# containing a fish using YOLOv8.
# Script currently uses a sample stock video for testing purposes.
# Sample video is incorrectly identified as multiple objects, but
# most often as a bird.
#------------------------------------------------------------------

# Import any nessecary libraries
from ultralytics import YOLO
from collections import Counter
import os
import shutil
import time

def analyze_videos():
    # Standard YOLOv8 model
    model = YOLO("yolov8n.pt") 

    for filename in os.listdir("sample_data/"):
        results = model.predict(
            source=os.path.join("sample_data", filename),
            show=False,      # Set to true to show analysis in realtime
            save=False,      # Set to true to save images/videos with detections drawn
            save_txt=False, # Set to true to save detection results as .txt files (One file per frame)
        project="results", 
        name="trial"        # Each video will have it's own folder (trial, trial2, etc) if save=True
        ) 

        # Frames with detections are added to an array and printed
        detections = []
        for frame_index, r in enumerate(results):
            for box in r.boxes:
                cls_id = int(box.cls)
                conf = float(box.conf)
                detections.append((frame_index, cls_id, conf))

        # Frames with detections are analyzed to determine what 
        # object the video most likely contains.
        # Note: This assumes the video contains only one type of object.
        ids = [d[1] for d in detections]
        id_counter = Counter(ids)
        most_common_id = id_counter.most_common(1)[0][0]
        most_common_class = model.names[most_common_id]

        if most_common_id == 14:
            folder = "has_fish"
        else:
            folder = "no_fish"

        category_dir = os.path.join("results", folder)
        os.makedirs(category_dir, exist_ok=True)

        shutil.copy(r.path, category_dir)

        # Average confidence is determined based on how often the most
        # common object is detected.
        confidence = [d[2] for d in detections if d[1] == most_common_id]
        avg_confidence = (sum(confidence) / len(confidence)) * 100

        # Results printed for analysis. 
        print("--------------------------------------------------------------")
        print(f"Video most likely contains a {most_common_class} (Confidence: {avg_confidence:.2f}%).")
        print("--------------------------------------------------------------")



