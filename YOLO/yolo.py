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
results = model.predict(
    source="sample_images/",
    show=True, 
    save=True, 
    project="results", 
    name="trial"
) 

# Displays image with bounding boxes
results[0].plot()