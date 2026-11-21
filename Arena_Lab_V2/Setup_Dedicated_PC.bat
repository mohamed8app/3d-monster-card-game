@echo off
title Arena Lab V2 - One-Time CPU Setup (Dedicated Machine)
cd /d "%~dp0"

echo Creating isolated Python virtual environment in .\venv ...
python -m venv venv
if errorlevel 1 (
    echo ERROR: Python 3.10+ not found on PATH. Please install Python 3.10+ first.
    pause
    exit /b 1
)

echo Upgrading pip and installing lightweight CPU-only PyTorch + NumPy + OpenPyXL ...
"%~dp0venv\Scripts\python.exe" -m pip install --upgrade pip
"%~dp0venv\Scripts\python.exe" -m pip install numpy openpyxl
"%~dp0venv\Scripts\python.exe" -m pip install torch --index-url https://download.pytorch.org/whl/cpu

echo.
echo Setup complete! Double-click Start_Trainer.bat or Start_Analytics.bat to launch.
pause
