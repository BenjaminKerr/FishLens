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

pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'


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

    # Common OCR issue on ASF overlay: extra digit in year (e.g., 20265/10/01...)
    # Convert 5-digit year starting with 20xxx to 4-digit year.
    text = re.sub(r'^(20\d{2})\d([/\-])', r'\1\2', text)

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
# Function: extract_timestamp_from_frame
# Description: Extract timestamp from bottom-left corner of video frame.
# Notes: N/A
def extractTimestamFromFrame(frame, debug=False):
    if frame is None or frame.size == 0:
        return None
    
    try:
        h, w = frame.shape[:2]
        
        # Focus on bottom-left corner where timestamp appears
        # Taking bottom 25% of height and left 65% of width
        regionHeight = int(h * 0.25)
        regionWidth = int(w * 0.65)
        
        # Extract the timestamp region
        timestampRegion = frame[h - regionHeight:h, 0:regionWidth]
        
       
        
        # Convert to grayscale
        gray = cv2.cvtColor(timestampRegion, cv2.COLOR_BGR2GRAY)
        
        # White text on dark background - use binary threshold
        _, thresh = cv2.threshold(gray, 150, 255, cv2.THRESH_BINARY)
        
        # Optional: Apply slight dilation to thicken text
        kernel = np.ones((2, 2), np.uint8)
        processed = cv2.dilate(thresh, kernel, iterations=1)
        
        
        # OCR configuration optimized for single-line timestamps
        # Whitelist only characters that appear in timestamps
        custom_config = r'--oem 3 --psm 7 -c tessedit_char_whitelist=0123456789/:- '
        
        # Perform OCR
        text = pytesseract.image_to_string(processed, config=custom_config)
        text = text.strip()
        
        if debug:
            print(f"  OCR raw output: '{text}'")
        
        if len(text) < 15:  # Timestamp should be at least 15 chars
            return None
        
        # Parse the timestamp
        timestamp = parseTimestamp(text)
        
        if timestamp and debug:
            print(f"  Parsed timestamp: {timestamp}")
        
        return timestamp
        
    except Exception as e:
        print(f"Error extracting timestamp: {e}")
        import traceback
        traceback.print_exc()
        return None
