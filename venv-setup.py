# ****************************************************************
# File: venv-setup.py
# Description: Setup script for virtual environment.
# Notes: This file will eventually be automated via a batch file
# or something, but for now it's manual. This file MUST BE RAN
# before testing any frontend-adjacent code. 
# ****************************************************************

import sys
import subprocess
import os

# Initialize paths
project_root = os.path.dirname(os.path.abspath(__file__))
python_path = os.path.join(project_root, 'Python')
venv_path = os.path.join(python_path, 'venv')
requirements_path = os.path.join(project_root, 'requirements.txt')

# Create venv path if it doesn't exist
if not os.path.exists(venv_path):
    print(f"Creating venv folder...")
    subprocess.run([os.path.join(python_path, "python.exe"), "-m", "venv", venv_path], check=True)
else:
    print(f"Virtual environment folder already exists--skipping creation.")

# Install all dependencies from requirements.txt
pip_exe = os.path.join(venv_path, 'Scripts', 'pip.exe')
print(f"Installing dependencies from requirements.txt...")
subprocess.run([pip_exe, "install", "--upgrade", "-r", requirements_path], check=True)
print("All dependencies installed.")
print("Setup complete.")
