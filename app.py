import streamlit as st
import pandas as pd
import numpy as np

st.set_page_config(page_title="FishLens", layout="wide")

st.title("FishLens")
st.header("FishLens")

with st.sidebar:
    st.subheader("Navigation")
    if st.button("Button"):
        st.write("Button clicked!")