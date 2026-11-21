@echo off
title Arena Lab V2 - Python and Packages Installer
setlocal
pushd "%~dp0"

echo ========================================================================
echo  ARENA LAB V2 - ONE-CLICK PYTHON + PACKAGES INSTALLER (CPU-ONLY)
echo  Folder: %cd%
echo ========================================================================
echo.

set "BASE_PY="

call :try_py python
if not "%BASE_PY%"=="" goto :found_py

call :try_py "py -3.12"
if not "%BASE_PY%"=="" goto :found_py

call :try_py "py -3.11"
if not "%BASE_PY%"=="" goto :found_py

call :try_py "py -3.10"
if not "%BASE_PY%"=="" goto :found_py

call :try_py "py -3"
if not "%BASE_PY%"=="" goto :found_py

call :try_exe "%LOCALAPPDATA%\Programs\Python\Python312\python.exe"
if not "%BASE_PY%"=="" goto :found_py

call :try_exe "%LOCALAPPDATA%\Programs\Python\Python311\python.exe"
if not "%BASE_PY%"=="" goto :found_py

call :try_exe "%LOCALAPPDATA%\Programs\Python\Python310\python.exe"
if not "%BASE_PY%"=="" goto :found_py

call :try_exe "C:\Python312\python.exe"
if not "%BASE_PY%"=="" goto :found_py

call :try_exe "C:\Python311\python.exe"
if not "%BASE_PY%"=="" goto :found_py

call :try_exe "C:\Python310\python.exe"
if not "%BASE_PY%"=="" goto :found_py

echo [!] Python 3.10+ with Tkinter was not found on this PC.
echo     Downloading official Python 3.11.9 64-bit installer...
echo.
set "PY_INST=%TEMP%\python-3.11.9-amd64.exe"
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe' -OutFile '%PY_INST%'"
if not exist "%PY_INST%" goto :dl_fail

echo Installing Python 3.11.9 silently - please wait 1 to 2 minutes...
"%PY_INST%" /quiet InstallAllUsers=0 PrependPath=1 Include_test=0 Include_pip=1 Include_tcltk=1
del "%PY_INST%" >nul 2>&1

if not exist "%LOCALAPPDATA%\Programs\Python\Python311\python.exe" goto :py_inst_fail
set "BASE_PY=%LOCALAPPDATA%\Programs\Python\Python311\python.exe"

:found_py
echo [1/4] Found Python with Tkinter:
echo       %BASE_PY%
"%BASE_PY%" --version
echo.

echo [2/4] Creating local virtual environment in .\venv ...
if not exist "%cd%\venv\Scripts\python.exe" "%BASE_PY%" -m venv "%cd%\venv"
if not exist "%cd%\venv\Scripts\python.exe" goto :venv_fail

set "VENV_PY=%cd%\venv\Scripts\python.exe"

echo.
echo [3/4] Installing numpy, openpyxl, and lightweight CPU-only PyTorch...
echo       Please wait while packages download and install...
"%VENV_PY%" -m pip install --upgrade pip
"%VENV_PY%" -m pip install numpy openpyxl
"%VENV_PY%" -m pip install torch --index-url https://download.pytorch.org/whl/cpu
if not errorlevel 1 goto :verify_step

echo Retrying PyTorch from default PyPI...
"%VENV_PY%" -m pip install torch
if errorlevel 1 goto :pip_fail

:verify_step
echo.
echo [4/4] Verifying Tkinter GUI, PyTorch CPU, NumPy, and 2,270-card database...
"%VENV_PY%" -c "import tkinter, numpy, torch, openpyxl; from engine.card_model import get_card_db; db = get_card_db(); print('VERIFIED OK -> PyTorch:', torch.__version__, '| NumPy:', numpy.__version__, '| Cards:', db.num_cards, '| Tkinter: OK')"
if errorlevel 1 goto :verify_fail

echo.
echo ========================================================================
echo  INSTALLATION COMPLETE! EVERYTHING IS READY!
echo  - Double-click  Start_Trainer.bat    to open Tkinter Trainer GUI
echo  - Double-click  Start_Analytics.bat  to open Tkinter Analytics GUI
echo ========================================================================
popd
pause
exit /b 0

:try_py
%~1 -c "import sys, tkinter; assert sys.version_info.major == 3 and sys.version_info.minor >= 10; print(sys.executable)" > "%TEMP%\_al2_py.txt" 2>nul
if errorlevel 1 goto :try_clean
set /p BASE_PY=<"%TEMP%\_al2_py.txt"
:try_clean
del "%TEMP%\_al2_py.txt" >nul 2>&1
exit /b 0

:try_exe
if not exist "%~1" exit /b 0
"%~1" -c "import sys, tkinter; assert sys.version_info.major == 3 and sys.version_info.minor >= 10; print(sys.executable)" > "%TEMP%\_al2_py.txt" 2>nul
if errorlevel 1 goto :try_clean2
set /p BASE_PY=<"%TEMP%\_al2_py.txt"
:try_clean2
del "%TEMP%\_al2_py.txt" >nul 2>&1
exit /b 0

:dl_fail
echo ERROR: Could not download Python installer. Check internet connection or install Python 3.11 from python.org.
popd
pause
exit /b 1

:py_inst_fail
echo ERROR: Python installation did not finish automatically. Install Python 3.11 from python.org with Add Python to PATH checked.
popd
pause
exit /b 1

:venv_fail
echo ERROR: Failed to create local virtual environment in .\venv
popd
pause
exit /b 1

:pip_fail
echo ERROR: Failed to install required Python packages. Check internet connection and try again.
popd
pause
exit /b 1

:verify_fail
echo ERROR: Package verification failed. Check error output above.
popd
pause
exit /b 1
