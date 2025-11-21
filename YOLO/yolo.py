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

# Standard YOLOv8 model
model = YOLO("yolov8n.pt") 

# Input folder: sample_images
# Output folder: results/trial1, where trial1 is auto-incremented
# each time the program is run.
# Warning: Setting save_txt to true will create a .txt file for each
# frame of video.
results = model.predict(
    source="sample_images/",
    show=True, 
    save=True, 
    save_txt=False,
    project="results", 
    name="trial"
) 

# Frames with high confidence are added to an array and printed
detections = []

for frame_index, r in enumerate(results):
    for box in r.boxes:
        cls_id = int(box.cls[0])
        conf = float(box.conf[0].item())
        conf_trunc = f"{conf:.2f}"
        if conf >= 0.5:
            detections.append((frame_index, cls_id, conf_trunc))

print (detections)

# Displays image with bounding boxes
results[0].plot()