"""
Module for extracting timestamps from video frames using OCR.
Author: Aleks
"""

import cv2
import numpy as np
import pytesseract
import re

pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'

def parse_timestamp(text):
    """Parse timestamp from OCR text with validation and common OCR error correction."""
    if not text:
        return None
    
    # Clean up text - remove spaces that shouldn't be there
    text = text.replace(' ', '')
    
    # Try regex patterns - more flexible to match the OCR output
    # Extended patterns to handle OCR errors like extra digits
    patterns = [
        r'(\d{4})[/\-](\d{2})[/\-](\d{2})(\d{2}):(\d{2,3}):(\d{2})',  # Allow 2-3 digits for minutes (OCR errors)
        r'(\d{4})[/\-](\d{2})[/\-](\d{2})(\d{2}):(\d{2}):(\d{2})',  # No space between date/time
        r'(\d{4})[/\-](\d{1,2})[/\-](\d{1,2})\s*(\d{1,2}):(\d{2,3}):(\d{2})',  # Allow 2-3 digits for minutes
        r'(\d{4})[/\-](\d{1,2})[/\-](\d{1,2})\s*(\d{1,2}):(\d{2}):(\d{2})',
        r'(\d{4})[/\-](\d{1,2})[/\-](\d{1,2})\s*(\d{1,2}):(\d{2})',
    ]
    
    for pattern in patterns:
        match = re.search(pattern, text)
        if match:
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
            
            # Validate ranges
            if not (2020 <= int(year) <= 2030):
                continue
            if not (1 <= month <= 12):
                continue
            if not (1 <= day <= 31):
                continue
            if not (0 <= hour <= 23):
                continue
            if not (0 <= minute <= 59):
                continue
            if not (0 <= second <= 59):
                continue
            
            # Format properly: YYYY/MM/DD HH:MM:SS
            return f"{year}/{month:02d}/{day:02d} {hour:02d}:{minute:02d}:{second:02d}"
    
    return None

def extract_timestamp_from_frame(frame, debug=False):
    """
    Extract timestamp from bottom-left corner of video frame.
    Format: YYYY/MM/DD HH:MM:SS (white text on dark background)
    Example: 2025/10/08 23:31:11
    """
    if frame is None or frame.size == 0:
        return None
    
    try:
        h, w = frame.shape[:2]
        
        # Focus on bottom-left corner where timestamp appears
        # Taking bottom 25% of height and left 65% of width
        region_height = int(h * 0.25)
        region_width = int(w * 0.65)
        
        # Extract the timestamp region
        timestamp_region = frame[h - region_height:h, 0:region_width]
        
       
        
        # Convert to grayscale
        gray = cv2.cvtColor(timestamp_region, cv2.COLOR_BGR2GRAY)
        
        # White text on dark background - use binary threshold
        _, thresh = cv2.threshold(gray, 150, 255, cv2.THRESH_BINARY)
        
        # Optional: Apply slight dilation to thicken text
        kernel = np.ones((2, 2), np.uint8)
        processed = cv2.dilate(thresh, kernel, iterations=1)
        
        if debug:
            cv2.imwrite("debug_timestamp_processed.jpg", processed)
        
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
        timestamp = parse_timestamp(text)
        
        if timestamp and debug:
            print(f"  ✓ Parsed timestamp: {timestamp}")
        
        return timestamp
        
    except Exception as e:
        print(f"Error extracting timestamp: {e}")
        import traceback
        traceback.print_exc()
        return None
