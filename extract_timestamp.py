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
import os
from datetime import datetime, timedelta
from collections import Counter

pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'

DIGIT_TEMPLATE_DIR = os.path.join(os.path.dirname(__file__), "ocr_digit_templates")
AMBIGUOUS_DIGITS = {"5", "6"}
TEMPLATE_SIZE = (20, 32)
TEMPLATE_SINGLE_MAX_DIFF = float(os.getenv("FISHLENS_DIGIT_TEMPLATE_MAX_DIFF", "35"))
TEMPLATE_SWITCH_MARGIN = float(os.getenv("FISHLENS_DIGIT_TEMPLATE_SWITCH_MARGIN", "6"))
_DIGIT_TEMPLATE_CACHE = None
_DIGIT_TEMPLATE_CACHE_MTIMES = None


def _normalize_digit_patch(patch):
    if patch is None or patch.size == 0:
        return None
    if len(patch.shape) == 3 and patch.shape[2] > 1:
        patch = cv2.cvtColor(patch, cv2.COLOR_BGR2GRAY)
    elif len(patch.shape) == 3 and patch.shape[2] == 1:
        patch = patch[:, :, 0]
    patch = cv2.resize(patch, TEMPLATE_SIZE, interpolation=cv2.INTER_AREA)
    _, patch = cv2.threshold(patch, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    return patch


def _load_digit_templates():
    global _DIGIT_TEMPLATE_CACHE, _DIGIT_TEMPLATE_CACHE_MTIMES
    if _DIGIT_TEMPLATE_CACHE is not None and _DIGIT_TEMPLATE_CACHE_MTIMES is not None:
        # If template files have not changed, reuse cache.
        unchanged = True
        for digit in AMBIGUOUS_DIGITS:
            found_path = None
            for ext in ("png", "PNG", "jpg", "JPG", "jpeg", "JPEG"):
                p = os.path.join(DIGIT_TEMPLATE_DIR, f"{digit}.{ext}")
                if os.path.exists(p):
                    found_path = p
                    break

            prev = _DIGIT_TEMPLATE_CACHE_MTIMES.get(digit)
            cur = os.path.getmtime(found_path) if found_path and os.path.exists(found_path) else None
            if prev != cur:
                unchanged = False
                break

        if unchanged:
            return _DIGIT_TEMPLATE_CACHE

    # Rebuild cache when first loading or when template files changed.
    if _DIGIT_TEMPLATE_CACHE is not None:
        _DIGIT_TEMPLATE_CACHE = None
        _DIGIT_TEMPLATE_CACHE_MTIMES = None

    if _DIGIT_TEMPLATE_CACHE is not None:
        return _DIGIT_TEMPLATE_CACHE

    templates = {}
    mtimes = {}
    for digit in AMBIGUOUS_DIGITS:
        path = None
        for ext in ("png", "PNG", "jpg", "JPG", "jpeg", "JPEG"):
            candidate = os.path.join(DIGIT_TEMPLATE_DIR, f"{digit}.{ext}")
            if os.path.exists(candidate):
                path = candidate
                break

        if not path:
            continue

        img = cv2.imread(path, cv2.IMREAD_GRAYSCALE)
        if img is None:
            continue

        normalized = _normalize_digit_patch(img)
        if normalized is not None:
            templates[digit] = normalized
            mtimes[digit] = os.path.getmtime(path)

    _DIGIT_TEMPLATE_CACHE = templates
    _DIGIT_TEMPLATE_CACHE_MTIMES = mtimes
    return _DIGIT_TEMPLATE_CACHE


def _resolve_ambiguous_digit(char_img, raw_char):
    templates = _load_digit_templates()
    if raw_char not in AMBIGUOUS_DIGITS or not templates:
        return raw_char

    normalized = _normalize_digit_patch(char_img)
    if normalized is None:
        return raw_char

    scores = {}
    for digit, template in templates.items():
        diff = cv2.absdiff(normalized, template)
        scores[digit] = float(np.mean(diff))

    if not scores:
        return raw_char

    if len(scores) == 1:
        only_digit, only_score = next(iter(scores.items()))
        # With only one template present, never force-convert to the other digit.
        # This avoids cases where missing 5.png causes 5 -> 6 flips.
        if only_digit == raw_char and only_score <= TEMPLATE_SINGLE_MAX_DIFF:
            return raw_char
        return raw_char

    best_digit = min(scores, key=scores.get)
    best_score = scores[best_digit]
    raw_score = scores.get(raw_char)

    # If the best template match is still poor, keep Tesseract's original digit.
    if best_score > TEMPLATE_SINGLE_MAX_DIFF:
        return raw_char

    # Keep original when it is already the best match.
    if best_digit == raw_char:
        return raw_char

    # Only switch digits when the winning template is clearly better.
    if raw_score is None or (raw_score - best_score) < TEMPLATE_SWITCH_MARGIN:
        return raw_char

    return best_digit


def _extract_text_from_boxes(processed, custom_config):
    """
    Build OCR text from per-character boxes and apply optional 5/6 disambiguation
    using user-supplied templates in ocr_digit_templates/5.png and 6.png.
    """
    try:
        boxes = pytesseract.image_to_boxes(processed, config=custom_config)
    except Exception:
        return ""

    if not boxes:
        return ""

    h, _ = processed.shape[:2]
    chars = []

    for line in boxes.splitlines():
        parts = line.strip().split()
        if len(parts) < 5:
            continue

        raw_char = parts[0]
        if not raw_char:
            continue
        raw_char = raw_char[0]

        try:
            x1, y1, x2, y2 = map(int, parts[1:5])
        except Exception:
            chars.append(raw_char)
            continue

        top = max(0, h - y2)
        bottom = min(h, h - y1)
        left = max(0, x1)
        right = max(left + 1, x2)
        char_img = processed[top:bottom, left:right]

        chars.append(_resolve_ambiguous_digit(char_img, raw_char))

    return "".join(chars).strip()


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

    parsed = []
    for probe_frame in candidates:
        result = extractTimestamFromFrame(probe_frame, False)
        if result and result[0]:  # result is (timestamp, confidence) tuple
            parsed.append(result[0])

    if not parsed:
        return None

    # Prefer a stable date first, then choose the earliest parsed timestamp on that date.
    # This mitigates persistent OCR 5/6 minute drift near the start of a video.
    date_counts = Counter(ts.split(' ')[0] for ts in parsed if ' ' in ts)
    best_date = date_counts.most_common(1)[0][0] if date_counts else None

    filtered = [ts for ts in parsed if (best_date is None or ts.startswith(best_date + ' '))]
    if not filtered:
        filtered = parsed

    parsed_datetimes = []
    for ts in filtered:
        try:
            parsed_datetimes.append(datetime.strptime(ts, "%Y/%m/%d %H:%M:%S"))
        except Exception:
            continue

    if parsed_datetimes:
        return min(parsed_datetimes).strftime("%Y/%m/%d %H:%M:%S")

    # Fallback to previous behavior when datetime parsing fails.
    full_counts = Counter(filtered)
    most_common_full, full_count = full_counts.most_common(1)[0]

    # If there is no clear full-timestamp winner, stabilize by date part and then choose
    # the most common timestamp within that date.
    if full_count == 1 and len(filtered) > 1:
        date_counts = Counter(ts.split(' ')[0] for ts in filtered if ' ' in ts)
        if date_counts:
            best_date = date_counts.most_common(1)[0][0]
            same_date = [ts for ts in filtered if ts.startswith(best_date + ' ')]
            if same_date:
                return Counter(same_date).most_common(1)[0][0]

    return most_common_full


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

    def _to_float(track, key, default=0.0):
        try:
            return float(track.get(key, default))
        except Exception:
            return default

    def _is_near_duplicate(a, b):
        # Must match timestamp exactly to even be considered.
        ta = str(a.get("video_timestamp", "")).strip()
        tb = str(b.get("video_timestamp", "")).strip()
        if not ta or ta.lower() == "not detected" or ta != tb:
            return False

        # Must be same detected class/direction to reduce accidental merges.
        if str(a.get("likely_class", "")).lower() != str(b.get("likely_class", "")).lower():
            return False

        da = str(a.get("direction", "")).lower()
        db = str(b.get("direction", "")).lower()
        if da and db and da != "unknown" and db != "unknown" and da != db:
            return False

        a_start = _to_float(a, "start_time_sec")
        a_end = _to_float(a, "end_time_sec")
        b_start = _to_float(b, "start_time_sec")
        b_end = _to_float(b, "end_time_sec")

        # Only collapse tracks that are effectively the same window.
        return abs(a_start - b_start) <= 0.35 and abs(a_end - b_end) <= 0.35

    kept = []
    for track in finished_tracks:
        replaced = False
        for i, existing in enumerate(kept):
            if _is_near_duplicate(existing, track):
                if _pct_value(track) > _pct_value(existing):
                    kept[i] = track
                replaced = True
                break
        if not replaced:
            kept.append(track)

    return kept


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
                normalized = f"{year}/{month:02d}/{day:02d} {hour:02d}:{minute:02d}:{second:02d}"

                # Future-date sanity check for common OCR confusion 5 <-> 6 in year.
                # If OCR yields a timestamp significantly in the future, prefer year-1
                # when it produces a plausible non-future date.
                try:
                    parsed_dt = datetime.strptime(normalized, "%Y/%m/%d %H:%M:%S")
                    now = datetime.now()
                    future_grace = now + timedelta(days=2)

                    yi = int(year)
                    if parsed_dt > future_grace and yi - 1 >= 2020:
                        corrected = f"{yi - 1:04d}/{month:02d}/{day:02d} {hour:02d}:{minute:02d}:{second:02d}"
                        corrected_dt = datetime.strptime(corrected, "%Y/%m/%d %H:%M:%S")
                        if corrected_dt <= future_grace:
                            return corrected
                except Exception:
                    pass

                return normalized
    
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
        
        # Focus on bottom-left corner where timestamp appears
        # Taking bottom 25% of height and left 65% of width
        regionHeight = int(h * 0.25)
        regionWidth = int(w * 0.65)
        
        # Extract the timestamp region
        timestampRegion = frame[h - regionHeight:h, 0:regionWidth]
        
       
        
        # Convert to grayscale
        gray = cv2.cvtColor(timestampRegion, cv2.COLOR_BGR2GRAY)
        
        # White text on dark background - use binary threshold.
        _, thresh = cv2.threshold(gray, 150, 255, cv2.THRESH_BINARY)

        # Build multiple OCR variants. Some overlays read better without dilation,
        # while others benefit from slight thickening.
        kernel = np.ones((2, 2), np.uint8)
        processed_variants = [
            ("base", thresh),
            ("dilate", cv2.dilate(thresh, kernel, iterations=1)),
        ]
        
        
        # OCR configuration optimized for single-line timestamps
        # Whitelist only characters that appear in timestamps
        custom_config = r'--oem 3 --psm 7 -c tessedit_char_whitelist=0123456789/:- '
        
        candidate_texts = []
        for variant_name, processed in processed_variants:
            text = pytesseract.image_to_string(processed, config=custom_config).strip()
            box_text = _extract_text_from_boxes(processed, custom_config)

            # Prefer raw OCR first, then per-character boxes as fallback.
            # Box-level template disambiguation can occasionally over-correct 5 -> 6 in
            # minute fields for some camera overlays.
            for candidate in (text, box_text):
                if candidate:
                    candidate_texts.append(candidate)

            if debug:
                print(f"  OCR raw output ({variant_name}): '{text}'")
                if box_text:
                    print(f"  OCR box output ({variant_name}): '{box_text}'")
        
        candidates = [t for t in dict.fromkeys(candidate_texts)]
        if not candidates:
            return None, None

        # Parse all OCR candidates, then choose best using conservative tie-breakers.
        parsed_results = []
        for idx, candidate in enumerate(candidates):
            if len(candidate) < 15:  # Timestamp should be at least 15 chars
                continue
            parsed = _parseTimestampWithConfidence(candidate)
            if parsed:
                parsed_results.append((idx, candidate, parsed[0], parsed[1]))

        if not parsed_results:
            return None, None

        # Keep earliest candidate by default (boxes first, then raw OCR).
        result = (parsed_results[0][2], parsed_results[0][3])

        # If both candidates parsed and differ only by year, keep smaller year.
        # This directly addresses persistent 5->6 year flips (e.g., 2025 vs 2026)
        # while preserving month/day/time agreement.
        if len(parsed_results) >= 2:
            ts_values = [r[2] for r in parsed_results]
            unique_ts = list(dict.fromkeys(ts_values))
            if len(unique_ts) >= 2:
                try:
                    parts = []
                    for ts in unique_ts[:2]:
                        d, t = ts.split(" ", 1)
                        y, m, day = d.split("/")
                        parts.append((int(y), m, day, t, ts))

                    a, b = parts[0], parts[1]
                    if a[1] == b[1] and a[2] == b[2] and a[3] == b[3] and abs(a[0] - b[0]) == 1:
                        chosen_ts = a[4] if a[0] < b[0] else b[4]
                        chosen_conf = None
                        for r in parsed_results:
                            if r[2] == chosen_ts:
                                chosen_conf = r[3]
                                break
                        if chosen_conf is not None:
                            result = (chosen_ts, chosen_conf)
                except Exception:
                    pass
        
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
