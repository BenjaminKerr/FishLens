# APP.PY: FishLens - A Streamlit frontend for the FishLens fish identifier

# Import necessary libraries
import os
import requests
import streamlit as st # Streamlit for web app
import pandas as pd # Pandas for data manipulation
import numpy as np # NumPy for numerical operations


st.set_page_config(page_title="Home", layout="wide") # Set page configuration
st.title("FishLens") # Title of the app

if 'current_index' not in st.session_state:
    st.session_state.current_index = 0
if 'vids' not in st.session_state:
    st.session_state.vids = []

# File uploader for videos
if not st.session_state.vids: # Initial upload
    vids = st.file_uploader("", accept_multiple_files=True, type=["mp4", "asf"])
    if vids:
        # Display uploaded videos
        st.session_state.vids = list(vids)

        # Save uploaded videos to SavedVids folder
        for vid in vids:
            name = vid.name
            with open(os.path.join("SavedVids", vid.name),"wb") as f:
                f.write(vid.getbuffer())
        st.rerun()
else: # Subsequent uploads
    vids = st.session_state.vids
    if st.button("Upload more videos"):
        new_vids = st.file_uploader("Drag and drop videos here:", accept_multiple_files=True, type=["mp4", "asf"])
        if new_vids:
            st.session_state.vids.extend(new_vids)
            vids = st.session_state.vids
            st.rerun()

# Video processing function (placeholder, alphabetizes for now)
@st.cache_data
def process_video(video):
    # Placeholder: process the video and return results
    vids = [
        f for f in vids.iterdir()
        if f.is_file() and f.suffix.lower() in {".mp4", ".asf"}
    ]
    return sorted(vids)

if 'current_index' not in st.session_state:
    st.session_state.current_index = 0

#vids = get_video_files(files)

if vids:
    st.title("Uploaded Videos")
    st.write(f"Video {st.session_state.current_index + 1} of {len(vids)}")
    current_vid = vids[st.session_state.current_index]
    st.write(current_vid.name) # Display current video
    st.video(current_vid) # Display current video

    with st.sidebar:
        st.header("All Videos")
        for idx, vid in enumerate(vids):
            if idx == st.session_state.current_index:
                st.markdown(f"**{vid.name}**")
            else:
                st.button(f"{idx + 1}. {vid.name}", key=vid.name, on_click=lambda i=idx: st.session_state.update({'current_index': i}))