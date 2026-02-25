# FishLens

Fish detection and tracking system using YOLO, DeepSort, and species classification.

## Setup Instructions

### 1. Install Python Dependencies

```bash
pip install -r requirements.txt
```

### 2. Install Tesseract OCR (Required for timestamp extraction)

**Automatic Installation (Windows):**
```powershell
powershell -ExecutionPolicy Bypass -File setup_ocr.ps1
```

**Manual Installation:**
- Download Tesseract OCR from: https://github.com/UB-Mannheim/tesseract/wiki
- Install to default location: `C:\Program Files\Tesseract-OCR\`
- If installed elsewhere, update the path in `extract_timestamp.py` and `main.py`

**Verify Installation:**
```bash
tesseract --version
```

### 3. Run the Application

```bash
python main.py [video_folder_path]
```

If no path is provided, it will process videos from the `sample_data` folder.

## Features

- **Fish Detection**: YOLO-based fish detection
- **Tracking**: DeepSort multi-object tracking
- **Species Classification**: Keras/TensorFlow CNN classifier
- **Timestamp Extraction**: OCR-based video timestamp reading
- **Direction Detection**: Upstream/downstream movement tracking
- **CSV Export**: Detailed tracking results with timestamps

## Output

Results are saved to:
- `fish_summary.csv` - Detection summary with timestamps and species
- `fish_images/` - Cropped images of detected fish
- `results/` - Processed videos and tracking data

Citations:
-------------------------------
YOLOv8
@software{yolov8_ultralytics,
  author = {Glenn Jocher and Ayush Chaurasia and Jing Qiu},
  title = {Ultralytics YOLOv8},
  version = {8.0.0},
  year = {2023},
  url = {https://github.com/ultralytics/ultralytics},
  orcid = {0000-0001-5950-6979, 0000-0002-7603-6750, 0000-0003-3783-7069},
  license = {AGPL-3.0}
}
-------------------------------
