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
    """Parse timestamp from OCR text."""
    if not text:
        return None
    
    # Clean up text - remove spaces that shouldn't be there
    text = text.replace(' ', '')
    
    # Try regex patterns - more flexible to match the OCR output
    patterns = [
        r'(\d{4})[/\-](\d{2})[/\-](\d{2})(\d{2}):(\d{2}):(\d{2})',  # No space between date/time
        r'(\d{4})[/\-](\d{1,2})[/\-](\d{1,2})\s*(\d{1,2}):(\d{2}):(\d{2})',
        r'(\d{4})[/\-](\d{1,2})[/\-](\d{1,2})\s*(\d{1,2}):(\d{2})',
    ]
    
    for pattern in patterns:
        match = re.search(pattern, text)
        if match:
            # Format properly: YYYY/MM/DD HH:MM:SS
            groups = match.groups()
            if len(groups) == 6:
                # Has date and time with seconds
                return f"{groups[0]}/{groups[1].zfill(2)}/{groups[2].zfill(2)} {groups[3].zfill(2)}:{groups[4]}:{groups[5]}"
            elif len(groups) == 5:
                # Has date and time without seconds
                return f"{groups[0]}/{groups[1].zfill(2)}/{groups[2].zfill(2)} {groups[3].zfill(2)}:{groups[4]}"
    
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
