from ultralytics import YOLO

model = YOLO("yolov8n.pt") 
# Standard YOLOv8 model

results = model.predict(source="https://ultralytics.com/images/bus.jpg", show=True, save=True, project="image_results", name="trial") 
# Analyzed images are exported into 'image_results/trial' with trial incrementing with each execution (e.g., trial1, trial2, etc.)

results[0].plot()
# Displays image with bounding boxes

