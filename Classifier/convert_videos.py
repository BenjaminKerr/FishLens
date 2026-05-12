"""
Convert source videos in Classifier/toPull into cleaner MP4 files.

This is mainly intended for older .asf clips that decode noisily when used
directly for crop extraction.
"""

import os
import shutil
import subprocess


BASE_DIR = os.path.dirname(os.path.abspath(__file__))
INPUT_DIR = os.path.join(BASE_DIR, "toPull")
OUTPUT_DIR = os.path.join(BASE_DIR, "converted")

SUPPORTED_EXTENSIONS = {".asf", ".avi", ".mp4", ".mov", ".mkv", ".wmv"}


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

    ffmpeg_path = shutil.which("ffmpeg")
    if not ffmpeg_path:
        print("[skip] ffmpeg was not found on PATH. Install ffmpeg to use this converter.")
        return False

    ffmpeg_cmd = [
        ffmpeg_path,
        "-hide_banner",
        "-loglevel", "warning",
        "-y",
        "-fflags", "+genpts+discardcorrupt",
        "-err_detect", "ignore_err",
        "-i", source_path,
        "-map", "0:v:0",
        "-an",
        "-avoid_negative_ts", "make_zero",
        "-c:v", "libx264",
        "-preset", "veryfast",
        "-crf", "23",
        "-movflags", "+faststart",
        output_path,
    ]

    try:
        result = subprocess.run(ffmpeg_cmd, capture_output=True, text=True, check=False)
    except Exception as exc:
        print(f"[skip] ffmpeg invocation failed for {os.path.basename(source_path)}: {exc}")
        return False

    if result.returncode == 0 and os.path.exists(output_path) and os.path.getsize(output_path) > 0:
        print(f"[ok] {os.path.basename(source_path)} -> {os.path.basename(output_path)} (ffmpeg)")
        return True

    if os.path.exists(output_path) and os.path.getsize(output_path) == 0:
        os.remove(output_path)

    stderr_tail = (result.stderr or "").strip()
    if stderr_tail:
        print(f"[skip] ffmpeg failed for {os.path.basename(source_path)}: {stderr_tail}")
    else:
        print(f"[skip] ffmpeg failed for {os.path.basename(source_path)}")
    return False


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
