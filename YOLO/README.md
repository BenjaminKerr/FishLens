YOLO Usage:
Videos to analyze should be placed in root->sample_data folder.
Videos will be sorted one-by-one:
Videos with "bird" as the most detected class will be in results->has_fish.
Videos with any other object as the most detected class will be in results->no_fish.

TODO (Not currently implemented--can cause unpredictable behavior):
-Duplicate filename handling (Will overwrite old file)
-Handling for videos with zero detections (Probably a division by zero error)
-Videos are currently copied for testing purposes. This will likely change to cutting and pasting in the future. 

