from ultralytics import YOLO
import numpy as np

class YoloDetector:
    def __init__(self, weights_path="YOLO/yolov8n.pt"):
        self.model = YOLO(weights_path)

    def detect(self, frame):
        """
        Returns detections in [x1, y1, x2, y2, conf, class_id] format.
        """
        results = self.model.predict(frame, verbose=False)
        
        detections = []
        for box in results[0].boxes:
            x1, y1, x2, y2 = box.xyxy[0].cpu().numpy()
            conf = float(box.conf)
            cls_id = int(box.cls)
            detections.append([x1, y1, x2, y2, conf, cls_id])

        return np.array(detections)
