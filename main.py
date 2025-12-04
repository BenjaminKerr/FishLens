import os
from YOLO.yolo import run_yolo

if len(os.sys.argv) > 1:
    FILEPATH = os.sys.argv[1]
else:
    FILEPATH = "sample_data/"

for file in os.listdir(FILEPATH):
    run_yolo(file, FILEPATH + file)