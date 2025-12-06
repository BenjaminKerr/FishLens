# This main represents the minimum integration that will be needed in the actual main.
# File path is passed from FE as argv[1]. 
# File name and path are needed seperately for different parts of YOLO function.
import os
import sys

# Ensure YOLO module can be found
script_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, script_dir)

from YOLO.yolo import run_yolo

if len(os.sys.argv) > 1:
    FILEPATH = os.sys.argv[1]
else:
    FILEPATH = "sample_data/"

for file in os.listdir(FILEPATH):
    run_yolo(file, FILEPATH + "\\" + file)