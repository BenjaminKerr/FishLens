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

