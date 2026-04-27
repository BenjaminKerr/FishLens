#------------------------------------------------------------------
# Name: YOLOv8 Image Analysis Script
# Authors: Aden Ratliff; Neil
# Last Edited: 11.20.2025
# Summary and Notes:
# This legacy helper analyzes a single video with YOLOv8 and appends
# a simple summary row to yolo_output.csv.
#------------------------------------------------------------------

import csv
from collections import Counter

from ultralytics import YOLO


def run_yolo(filename, filepath):
    output_path = "yolo_output.csv"

    model = YOLO("yolov8n.pt")

    results = model.predict(
        source=filepath,
        stream=True,
        show=True,
        save=True,
        save_txt=False,
        vid_stride=10,
        project="results",
        name="trial",
    )

    detections = []
    for frame_index, result in enumerate(results):
        for box in result.boxes:
            cls_id = int(box.cls)
            conf = float(box.conf)
            detections.append((frame_index, cls_id, conf))

    if not detections:
        return None

    ids = [detection[1] for detection in detections]
    most_common_id = Counter(ids).most_common(1)[0][0]
    most_common_class = model.names[most_common_id]

    confidences = [detection[2] for detection in detections if detection[1] == most_common_id]
    avg_confidence = (sum(confidences) / len(confidences)) * 100

    print("--------------------------------------------------------------")
    print(f"Video most likely contains a {most_common_class} (Confidence: {avg_confidence:.2f}%).")
    print("--------------------------------------------------------------")

    with open(output_path, mode="a", newline="") as file:
        writer = csv.writer(file)
        writer.writerow([filename, most_common_class, f"{avg_confidence:.2f}%"])

    return most_common_class, avg_confidence
