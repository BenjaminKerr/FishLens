# APP.PY: FishLens - A Streamlit frontend for the FishLens fish identifier

# Import necessary libraries
import streamlit as st # Streamlit for web app
import pandas as pd # Pandas for data manipulation
import numpy as np # NumPy for numerical operations

st.set_page_config(page_title="FishLens", layout="wide") # Set page configuration
st.title("FishLens") # Title of the app

# File uploader for videos
vids = st.file_uploader("Drag and drop videos here:", accept_multiple_files=True, type=["mp4", "asf"])
for vid in vids:
    st.video(vid) # Display uploaded videos

# Sidebar for navigation
with st.sidebar:
    st.button("Home") # Home page
    st.button("History") # History page
    st.button("Settings") # Settings page