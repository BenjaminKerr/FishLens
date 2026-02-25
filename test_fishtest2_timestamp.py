import cv2
from extract_timestamp import extract_timestamp_from_frame

cap = cv2.VideoCapture(r'sample_data\FishTest2.mp4')
fps = cap.get(cv2.CAP_PROP_FPS)
print(f'FPS: {fps}')
print('\nTesting multiple frames:')

# Test frames at different positions
for i in [0, 1, 30, 60, 150, 300]:
    cap.set(cv2.CAP_PROP_POS_FRAMES, i)
    ret, frame = cap.read()
    if ret:
        timestamp = extract_timestamp_from_frame(frame, debug=(i==300))
        print(f'Frame {i} ({i/fps:.2f}s): {timestamp}')
    else:
        print(f'Frame {i}: Could not read')

cap.release()
