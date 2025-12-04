import os
from YOLO.yolo import run_yolo

FILEPATH = "sample_data/"

for file in os.listdir(FILEPATH):
    run_yolo(file)