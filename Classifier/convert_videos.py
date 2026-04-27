"""
Convert source videos in Classifier/toPull into cleaner MP4 files.

This is mainly intended for older .asf clips that decode noisily when used
directly for crop extraction.
"""

import os
import cv2


BASE_DIR = os.path.dirname(os.path.abspath(__file__))
INPUT_DIR = os.path.join(BASE_DIR, "toPull")
OUTPUT_DIR = os.path.join(BASE_DIR, "converted")

SUPPORTED_EXTENSIONS = {".asf", ".avi", ".mp4", ".mov", ".mkv", ".wmv"}
OUTPUT_FPS_FALLBACK = 30.0


def iter_source_videos(input_dir):
    for name in sorted(os.listdir(input_dir)):
        path = os.path.join(input_dir, name)
        if not os.path.isfile(path):
            continue

        _, ext = os.path.splitext(name)
        if ext.lower() in SUPPORTED_EXTENSIONS:
            yield path


def convert_video(source_path, output_dir):
    base_name = os.path.splitext(os.path.basename(source_path))[0]
    output_path = os.path.join(output_dir, f"{base_name}.mp4")

    capture = cv2.VideoCapture(source_path)
    if not capture.isOpened():
        print(f"[skip] Could not open: {source_path}")
        return False

    width = int(capture.get(cv2.CAP_PROP_FRAME_WIDTH)) or 0
    height = int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT)) or 0
    fps = float(capture.get(cv2.CAP_PROP_FPS) or 0.0)

    if width <= 0 or height <= 0:
        print(f"[skip] Invalid video size: {source_path}")
        capture.release()
        return False

    if fps <= 1.0:
        fps = OUTPUT_FPS_FALLBACK

    writer = cv2.VideoWriter(
        output_path,
        cv2.VideoWriter_fourcc(*"mp4v"),
        fps,
        (width, height),
    )

    if not writer.isOpened():
        print(f"[skip] Could not create output: {output_path}")
        capture.release()
        return False

    frames_written = 0
    while True:
        success, frame = capture.read()
        if not success:
            break
        writer.write(frame)
        frames_written += 1

    capture.release()
    writer.release()

    if frames_written == 0:
        if os.path.exists(output_path):
            os.remove(output_path)
        print(f"[skip] No frames written: {source_path}")
        return False

    print(f"[ok] {os.path.basename(source_path)} -> {os.path.basename(output_path)} ({frames_written} frames)")
    return True


def main():
    os.makedirs(INPUT_DIR, exist_ok=True)
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    videos = list(iter_source_videos(INPUT_DIR))
    if not videos:
        print(f"No source videos found in: {INPUT_DIR}")
        return

    print(f"Converting {len(videos)} video(s) from {INPUT_DIR} to {OUTPUT_DIR}")

    converted = 0
    for source_path in videos:
        if convert_video(source_path, OUTPUT_DIR):
            converted += 1

    print(f"Finished. Converted {converted} of {len(videos)} video(s).")


if __name__ == "__main__":
    main()
