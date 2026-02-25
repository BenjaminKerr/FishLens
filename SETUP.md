# FishLens Setup Guide

Complete setup instructions for new team members.

## Prerequisites

- Python 3.12+ installed
- Windows 10/11 (or appropriate OS for your platform)
- Git (for cloning the repository)

## Quick Start (Windows)

### Step 1: Clone the Repository
```bash
git clone <repository-url>
cd FishLens
```

### Step 2: Install Python Dependencies
```bash
pip install -r requirements.txt
```

### Step 3: Install Tesseract OCR (Automatic)
```powershell
powershell -ExecutionPolicy Bypass -File setup_ocr.ps1
```

This script will:
- Check if Tesseract is already installed
- Download the Tesseract installer (v5.3.3)
- Install it silently to `C:\Program Files\Tesseract-OCR\`
- Verify the installation

### Step 4: Verify Everything Works
```bash
python -c "import pytesseract; import cv2; print('✓ All dependencies loaded')"
```

### Step 5: Run the Application
```bash
python main.py
```

---

## Manual Tesseract Installation

If the automatic script fails or you prefer manual installation:

### Windows
1. Download from: https://github.com/UB-Mannheim/tesseract/wiki
2. Run the installer (tesseract-ocr-w64-setup-*.exe)
3. Install to: `C:\Program Files\Tesseract-OCR\`
4. The installer adds it to PATH automatically

### macOS
```bash
brew install tesseract
```

Then update these files to use the correct path:
- `extract_timestamp.py` line 11
- `main.py` line 30

Change from:
```python
pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'
```

To (macOS):
```python
pytesseract.pytesseract.tesseract_cmd = '/opt/homebrew/bin/tesseract'  # or /usr/local/bin/tesseract
```

### Linux (Ubuntu/Debian)
```bash
sudo apt-get update
sudo apt-get install tesseract-ocr
```

Then update the path to:
```python
pytesseract.pytesseract.tesseract_cmd = '/usr/bin/tesseract'
```

---

## Testing Your Setup

### Test 1: Check Tesseract Installation
```bash
tesseract --version
```
Expected output: Tesseract version info

### Test 2: Test OCR Extraction
```bash
python -c "from extract_timestamp import extract_timestamp_from_frame; print('✓ OCR module loaded')"
```

### Test 3: Process a Sample Video
```bash
python main.py sample_data
```

Check for output in:
- `fish_summary.csv` - Should contain detection results with timestamps
- `fish_images/` - Cropped fish images

---

## Troubleshooting

### "No module named 'cv2'"
```bash
pip install opencv-python
```

### "TesseractNotFoundError"
- Verify Tesseract is installed: `tesseract --version`
- Check the path in `extract_timestamp.py` matches your installation
- Run the setup script again: `powershell -ExecutionPolicy Bypass -File setup_ocr.ps1`

### "No module named 'extract_timestamp'"
- Make sure you're running from the FishLens directory
- Check that `extract_timestamp.py` exists in the project root

### OCR Returns "Not detected"
- Verify Tesseract installation with: `tesseract --version`
- Check video has timestamp overlay in bottom-left corner
- Run with debug mode to see OCR output

---

## What Gets Installed

### Python Packages (from requirements.txt)
- **pytesseract**: Python wrapper for Tesseract OCR
- **opencv-python**: Video/image processing
- **torch/torchvision**: YOLO deep learning
- **tensorflow/keras**: Species classification
- **ultralytics**: YOLOv8 framework
- **deep_sort_realtime**: Object tracking
- Plus supporting libraries (numpy, pandas, etc.)

### System-Level Software
- **Tesseract OCR**: Open-source OCR engine for reading timestamps from video frames
  - Installation location: `C:\Program Files\Tesseract-OCR\` (Windows)
  - Size: ~60 MB
  - License: Apache 2.0

---

## For Team Leads

To ensure smooth onboarding:

1. ✓ All Python dependencies are in `requirements.txt`
2. ✓ Automated OCR setup script: `setup_ocr.ps1`
3. ✓ Documentation in `README.md` and `SETUP.md`
4. ✓ Sample data in `sample_data/` for testing

New team members just need to:
```bash
pip install -r requirements.txt
powershell -ExecutionPolicy Bypass -File setup_ocr.ps1
python main.py
```
