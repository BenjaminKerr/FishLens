# ****************************************************************
# File: venv-setup.py
# Description: Setup script for virtual environment.
# Notes: This file will eventually be automated via a batch file
# or something, but for now it's manual. This file MUST BE RAN
# before testing any other code.
# 
# #########################
# INSTALLATION INSTRUCTIONS
# #########################
# 1. Ensure you have Python 3.12.10 installed and selected (run python --version to check).
# 2. Run this script to create a virtual environment and install dependencies.
# 3. After script finishes, run the following command in VS Code Terminal:
#    venv\Scripts\activate 
#    - May have to run "Set-ExecutionPolicy -ExecutionPolicy -Scope Process -ExecutionPolicy Bypass" in PowerShell first.
# 4. In the Active Interpreter selection, navigate to the venv Python executable:
#    venv\Scripts\python.exe
# 5. Run trainer.py as well to create the FSI model before running the full program.
# ****************************************************************

import sys
import subprocess
import os

# Initialize paths
project_root = os.path.dirname(os.path.abspath(__file__))
venv_path = os.path.join(project_root, 'venv')
requirements_path = os.path.join(project_root, 'requirements.txt')
venv_python = os.path.join(venv_path, 'Scripts', 'python.exe')

# Create venv path if it doesn't exist
if not os.path.exists(venv_path):
    print(f"Creating venv folder...")
    subprocess.run([sys.executable, "-m", "venv", venv_path], check=True)
    print(f"venv folder created.")
else:
    print(f"Virtual environment folder already exists--skipping creation.")

# Install all dependencies from requirements.txt
print(f"Installing dependencies from requirements.txt...")
subprocess.run([venv_python, "-m", "pip", "install", "--upgrade", "-r", requirements_path], check=True)
print("All dependencies installed.")
print("Setup complete.")
