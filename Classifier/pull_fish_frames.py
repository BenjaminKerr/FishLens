"""
Pull cropped fish screenshots from videos in Classifier/converted using YOLO + DeepSort.

For each tracked fish, saves one crop every N frames into Classifier/pulled.
This is meant as a quick data-pulling helper for classifier training.
"""

from pathlib import Path
import sys

import cv2
from ultralytics import YOLO


BASE_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = BASE_DIR.parent
INPUT_DIR = BASE_DIR / "converted"
OUTPUT_DIR = BASE_DIR / "pulled"
MODEL_PATH = PROJECT_ROOT / "models" / "fish_detector4.pt"

if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from tracking.deepsort_tracker import DeepSortTracker

VIDEO_EXTENSIONS = {".mp4", ".avi", ".mov", ".mkv", ".asf", ".wmv", ".flv", ".webm"}
YOLO_CONFIDENCE_THRESHOLD = 0.32
MIN_DETECTION_BOX_AREA = 225
PROCESS_EVERY_N_FRAMES = 3
BOX_MARGIN_RATIO = 0.60
MIN_CROP_SIZE = 220


def is_video_file(path: Path) -> bool:
    return path.is_file() and path.suffix.lower() in VIDEO_EXTENSIONS


def ensure_directories() -> None:
    INPUT_DIR.mkdir(parents=True, exist_ok=True)
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)


def detect_fish(model: YOLO, frame):
    results = model.predict(
        source=frame,
        verbose=False,
        stream=False,
        save=False,
        conf=YOLO_CONFIDENCE_THRESHOLD,
    )

    detections = []
    if not results:
        return detections

    for box in results[0].boxes:
        x1, y1, x2, y2 = map(int, box.xyxy[0].cpu().numpy())
        conf_arr = box.conf.cpu().numpy()
        conf = float(conf_arr[0]) if conf_arr.size > 0 and conf_arr[0] is not None else 0.0
        cls_arr = box.cls.cpu().numpy()
        cls_id = int(cls_arr[0]) if cls_arr.size > 0 and cls_arr[0] is not None else -1

        if (x2 - x1) * (y2 - y1) < MIN_DETECTION_BOX_AREA:
            continue

        try:
            cls_name = str(model.names[cls_id]).lower()
        except Exception:
            cls_name = str(cls_id)

        if "fish" not in cls_name:
            continue

        detections.append([x1, y1, x2, y2, conf, cls_id])

    return detections


def crop_with_margin(frame, bbox):
    x1, y1, x2, y2 = bbox
    h, w = frame.shape[:2]
    box_w = x2 - x1
    box_h = y2 - y1
    margin_x = int(box_w * BOX_MARGIN_RATIO)
    margin_y = int(box_h * BOX_MARGIN_RATIO)

    left = x1 - margin_x
    top = y1 - margin_y
    right = x2 + margin_x
    bottom = y2 + margin_y

    crop_w = right - left
    crop_h = bottom - top

    if crop_w < MIN_CROP_SIZE:
        extra_w = (MIN_CROP_SIZE - crop_w) // 2
        left -= extra_w
        right += (MIN_CROP_SIZE - crop_w) - extra_w

    if crop_h < MIN_CROP_SIZE:
        extra_h = (MIN_CROP_SIZE - crop_h) // 2
        top -= extra_h
        bottom += (MIN_CROP_SIZE - crop_h) - extra_h

    left = max(0, left)
    top = max(0, top)
    right = min(w, right)
    bottom = min(h, bottom)

    return frame[top:bottom, left:right]


def process_video(model: YOLO, video_path: Path) -> int:
    tracker = DeepSortTracker()
    cap = cv2.VideoCapture(str(video_path))
    if not cap.isOpened():
        print(f"Could not open video: {video_path.name}")
        return 0

    saved_count = 0
    frame_index = 0
    video_stem = video_path.stem

    while True:
        ret, frame = cap.read()
        if not ret or frame is None:
            break

        if frame_index % PROCESS_EVERY_N_FRAMES != 0:
            frame_index += 1
            continue

        detections = detect_fish(model, frame)
        detections = tracker.filterOverlaps(detections)
        tracked = tracker.update(detections, frame)

        for obj in tracked:
            track_id = obj["trackId"]

            crop = crop_with_margin(frame, obj["bbox"])
            if crop is None or crop.size == 0:
                continue

            filename = f"{video_stem}_track{track_id}_frame{frame_index:06d}.jpg"
            output_path = OUTPUT_DIR / filename
            if cv2.imwrite(str(output_path), crop):
                saved_count += 1

        frame_index += 1

    cap.release()
    print(f"{video_path.name}: saved {saved_count} crops")
    return saved_count


def main():
    ensure_directories()

    if not MODEL_PATH.exists():
        raise FileNotFoundError(f"YOLO model not found: {MODEL_PATH}")

    videos = [path for path in sorted(INPUT_DIR.iterdir()) if is_video_file(path)]
    if not videos:
        print(f"No videos found in {INPUT_DIR}")
        return

    model = YOLO(str(MODEL_PATH))
    total_saved = 0

    for video_path in videos:
        total_saved += process_video(model, video_path)

    print(f"Done. Saved {total_saved} crops into {OUTPUT_DIR}")


if __name__ == "__main__":
    main()
