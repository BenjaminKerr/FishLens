# ****************************************************************
# File: extract_timestamp.py
# Description: Module for extracting timestamps from video frames using OCR.
# Author: Aleksen Thayer
# Notes: N/A
# ****************************************************************
import cv2
import numpy as np
import pytesseract
import re
from datetime import datetime
from collections import Counter

pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'


# ****************************************************************
# Function: check_tesseract
# Description: Check if Tesseract OCR is available and accessible.
# Notes: N/A
def check_tesseract():
    try:
        version = pytesseract.get_tesseract_version()
        print(f"Tesseract OCR detected: v{version}")
        return True
    except Exception as e:
        print(f"    WARNING: Tesseract OCR not found or not configured")
        print(f"   Timestamp extraction will be disabled")
        print(f"   Error: {e}")
        return False


# ****************************************************************
# Function: probe_video_timestamp
# Description: Probe early frames to get a stable video-level timestamp.
# Notes: Accepts optional read_frame_fn for callers that wrap cap.read().
def probe_video_timestamp(cap, first_frame, probe_frames=12, read_frame_fn=None):
    candidates = []

    if first_frame is not None:
        candidates.append(first_frame)

    read_fn = read_frame_fn if read_frame_fn is not None else (lambda c: c.read())

    for _ in range(max(1, probe_frames) - 1):
        ret, probe_frame = read_fn(cap)
        if not ret or probe_frame is None:
            break
        candidates.append(probe_frame)

    # Rewind so normal processing still starts from frame 0.
    cap.set(cv2.CAP_PROP_POS_FRAMES, 0)

    parsed = []  # list of (timestamp_str, confidence_str)
    for i, probe_frame in enumerate(candidates):
        result = extractTimestamFromFrame(probe_frame, False)
        if result and result[0]:  # result is (timestamp, confidence) tuple
            parsed.append(result)

    if not parsed:
        return None, None

    # Prefer the most common full timestamp string.
    ts_counts = Counter(ts for ts, _ in parsed)
    most_common_full, full_count = ts_counts.most_common(1)[0]

    # If there is no clear full-timestamp winner, stabilize by date part.
    if full_count == 1 and len(parsed) > 1:
        date_counts = Counter(ts.split(' ')[0] for ts, _ in parsed if ' ' in ts)
        if date_counts:
            best_date = date_counts.most_common(1)[0][0]
            same_date = [(ts, conf) for ts, conf in parsed if ts.startswith(best_date + ' ')]
            if same_date:
                winner_ts = Counter(ts for ts, _ in same_date).most_common(1)[0][0]
                # HIGH if any read of this timestamp was HIGH.
                winner_conf = "HIGH" if any(c == "HIGH" for ts, c in same_date if ts == winner_ts) else "MEDIUM"
                return winner_ts, winner_conf

    # Aggregate confidence for the winning timestamp string.
    winner_conf = "HIGH" if any(c == "HIGH" for ts, c in parsed if ts == most_common_full) else "MEDIUM"
    return most_common_full, winner_conf


# ****************************************************************
# Function: dedupe_tracks_by_timestamp
# Description: Remove duplicate tracks with same exact timestamp, keeping highest confidence.
# Notes: Tracks with missing timestamps are left unchanged.
def dedupe_tracks_by_timestamp(finished_tracks):
    if not finished_tracks:
        return finished_tracks

    def _pct_value(track):
        value = track.get("avg_confidence") or track.get("confidence") or "0%"
        try:
            return float(str(value).replace("%", "").strip())
        except Exception:
            return 0.0

    best_by_timestamp = {}
    passthrough = []

    for track in finished_tracks:
        ts = str(track.get("video_timestamp", "")).strip()
        if not ts or ts.lower() == "not detected":
            passthrough.append(track)
            continue

        existing = best_by_timestamp.get(ts)
        if existing is None or _pct_value(track) > _pct_value(existing):
            best_by_timestamp[ts] = track

    # Keep original relative ordering of selected tracks where possible.
    selected_ids = {id(t) for t in best_by_timestamp.values()}
    ordered_selected = [t for t in finished_tracks if id(t) in selected_ids]

    return ordered_selected + passthrough


# ****************************************************************
# Function: _normalize_ocr_year_prefix
# Description: Normalize noisy 5-digit OCR year prefixes to the most plausible 4-digit year.
# Notes: Handles cases like 20256/ and 20265/. Prefers earlier years when multiple valid candidates exist.
def _normalize_ocr_year_prefix(text):
    m = re.match(r'^(20\d{3})([/\-])', text)
    if not m:
        return text

    raw_year = m.group(1)
    sep = m.group(2)

    candidates = []
    for i in range(len(raw_year)):
        y = raw_year[:i] + raw_year[i + 1:]
        if len(y) == 4 and y.startswith("20"):
            yi = int(y)
            if 2020 <= yi <= 2035:
                candidates.append(yi)

    if not candidates:
        return text

    # Pick the earliest (smallest) year to handle OCR artifacts that add extra digits.
    # For 20256: candidates [2026, 2025] -> pick 2025
    candidates.sort()
    best_year = str(candidates[0])

    return best_year + sep + text[m.end():]


# ****************************************************************
# Function: parse_timestamp
# Description: Parse timestamp from OCR text with validation and common OCR error correction.
# Notes: N/A
def parseTimestamp(text):
    if not text:
        return None
    
    # Keep only chars expected in timestamps.
    text = re.sub(r'[^0-9/\-:\s]', '', text).strip()
    text = re.sub(r'\s+', ' ', text)

    # Normalize OCR-distorted year prefixes.
    text = _normalize_ocr_year_prefix(text)

    # Also handle no-space date/time form before matching.
    compact_text = text.replace(' ', '')

    # Match full string only (prevents partial matches like ...:07:60 becoming ...:07)
    patterns = [
        r'^(20\d{2})[/\-](\d{1,2})[/\-](\d{1,2})(\d{1,2}):(\d{2,3}):(\d{2})$',
        r'^(20\d{2})[/\-](\d{1,2})[/\-](\d{1,2})(\d{1,2}):(\d{2,3})$',
        r'^(20\d{2})[/\-](\d{1,2})[/\-](\d{1,2})\s*(\d{1,2}):(\d{2,3}):(\d{2})$',
        r'^(20\d{2})[/\-](\d{1,2})[/\-](\d{1,2})\s*(\d{1,2}):(\d{2,3})$',
    ]
    
    for src in (text, compact_text):
        for pattern in patterns:
            match = re.match(pattern, src)
            if not match:
                continue

            groups = match.groups()
            
            # Parse into components
            year = groups[0]
            month = int(groups[1])
            day = int(groups[2])
            hour = int(groups[3])
            minute = int(groups[4])
            second = int(groups[5]) if len(groups) == 6 else 0
            
            # Fix common OCR errors in minutes
            # Handle 3-digit minutes (e.g., 688 -> 58)
            if minute >= 100:
                # Extract the last two digits and check if reasonable
                # 688 -> take middle two: 68 -> 58
                minute_str = str(minute)
                if len(minute_str) == 3:
                    # Try middle two digits
                    minute = int(minute_str[0:2])
                    
            # Handle 2-digit minute errors (68 -> 58, 69 -> 59, etc.)
            if minute >= 60:
                # Common OCR mistake: 5 read as 6
                if 60 <= minute <= 69:
                    minute = minute - 10
                # Common OCR mistake: 4 read as 9  
                elif 90 <= minute <= 99:
                    minute = minute - 50
            
            # Fix occasional OCR seconds errors like 60-69 -> 00-09.
            if second >= 60 and 60 <= second <= 69:
                second = second - 60

            # Validate ranges - return formatted timestamp if all valid
            if (2020 <= int(year) <= 2030 and
                1 <= month <= 12 and
                1 <= day <= 31 and
                0 <= hour <= 23 and
                0 <= minute <= 59 and
                0 <= second <= 59):
                # Format properly: YYYY/MM/DD HH:MM:SS
                return f"{year}/{month:02d}/{day:02d} {hour:02d}:{minute:02d}:{second:02d}"
    
    return None

# ****************************************************************
# Function: extractTimestamFromFrame
# Description: Extract timestamp from bottom-left corner of video frame.
# Returns: tuple (timestamp_str, confidence) where confidence is 'HIGH' or 'MEDIUM'
def extractTimestamFromFrame(frame, debug=False):
    if frame is None or frame.size == 0:
        return None, None
    
    try:
        h, w = frame.shape[:2]
        
        # The bottom ~10% is a black letterbox border with no text.
        # The timestamp OSD sits in the 15% band just above that border.
        bottomSkip = int(h * 0.10)
        regionHeight = int(h * 0.15)   # 15% band above the border
        regionWidth = int(w * 0.65)

        # Extract the timestamp region (skip the black border at the very bottom).
        timestampRegion = frame[h - bottomSkip - regionHeight : h - bottomSkip, 0:regionWidth]

        # Upscale 2× — improves OCR accuracy on small/compressed text.
        timestampRegion = cv2.resize(
            timestampRegion,
            (regionWidth * 2, regionHeight * 2),
            interpolation=cv2.INTER_CUBIC,
        )

        # Convert to grayscale
        gray = cv2.cvtColor(timestampRegion, cv2.COLOR_BGR2GRAY)

        # Fixed threshold at 160: OSD text is near-255 white; anything above
        # 160 is confidently text, everything darker is background.
        # THRESH_BINARY_INV produces dark text on white directly, which is
        # what Tesseract expects — no separate bitwise_not needed.
        _, processed = cv2.threshold(gray, 160, 255, cv2.THRESH_BINARY_INV)
        
        
        # OCR configuration optimized for single-line timestamps
        # Whitelist only characters that appear in timestamps
        custom_config = r'--oem 3 --psm 7 -c tessedit_char_whitelist=0123456789/:- '
        
        # Perform OCR
        text = pytesseract.image_to_string(processed, config=custom_config)
        text = text.strip()
        
        if debug:
            print(f"  OCR raw output: '{text}'")
        
        if len(text) < 15:  # Timestamp should be at least 15 chars
            return None, None
        
        # Parse the timestamp - gets (timestamp, confidence) tuple
        result = _parseTimestampWithConfidence(text)
        
        if result and result[0] and debug:
            print(f"  Parsed timestamp: {result[0]} (confidence: {result[1]})")
        
        return result if result else (None, None)
        
    except Exception as e:
        print(f"Error extracting timestamp: {e}")
        import traceback
        traceback.print_exc()
        return None, None


# ****************************************************************
# Function: _parseTimestampWithConfidence
# Description: Parse timestamp and return (timestamp_str, confidence_level)
# Returns: tuple (timestamp_str, 'HIGH'|'MEDIUM'|None) or None
def _parseTimestampWithConfidence(text):
    timestamp = parseTimestamp(text)
    if not timestamp:
        return None
    
    # Check if any corrections were applied
    # Corrections indicate MEDIUM confidence
    has_5digit_year = re.search(r'20\d{3}[/\-]', text)
    
    if has_5digit_year:
        return (timestamp, 'MEDIUM')
    else:
        return (timestamp, 'HIGH')
