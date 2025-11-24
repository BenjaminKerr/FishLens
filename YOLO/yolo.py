#------------------------------------------------------------------
# Name: YOLOv8 Image Analysis Script
# Authors: Aden Ratliff; Neil
# Last Edited: 11.20.2025
# Summary and Notes:
# This script analyses videos to detect the probability of their
# containing a fish using YOLOv8.
#
# Script currently uses a sample stock video for testing purposes.
# Sample video is incorrectly identified as multiple objects, but
# most often as a bird.
#------------------------------------------------------------------

# Import any nessecary libraries
from ultralytics import YOLO
from collections import Counter

# Standard YOLOv8 model
model = YOLO("yolov8n.pt") 

# Input folder: sample_data
# Output folder: results/trial1, where trial1 is auto-incremented
# each time the program is run.
# Warning: Setting save_txt to true will create a .txt file for each
# vid_stride: Changes how many frames to skip before analyzing video
# frame of video.
results = model.predict(
    source="sample_data/",
    show=True, 
    save=False, 
    save_txt=False,
    vid_stride=20,
    project="results", 
    name="trial"
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

# Average confidence is determined based on how often the most
# common object is detected.
confidence = [d[2] for d in detections if d[1] == most_common_id]
avg_confidence = (sum(confidence) / len(confidence)) * 100

# Results printed for analysis. 
print("--------------------------------------------------------------")
print(f"Video most likely contains a {most_common_class} (Confidence: {avg_confidence:.2f}%).")
print("--------------------------------------------------------------")