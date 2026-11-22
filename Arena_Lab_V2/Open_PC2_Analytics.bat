@echo off
title Arena Lab V2 - Live Analytics Viewer (PC #2)
pushd "%~dp0"

if not exist "Y:\logs" (
    call "%~dp0LAN_Setup\3_Connect_MainPC_To_PC2.bat" --silent
)

set "PY_EXE="
if exist "%cd%\venv\Scripts\python.exe" set "PY_EXE=%cd%\venv\Scripts\python.exe"
if "%PY_EXE%"=="" if exist "C:\Arena_Lab_V2_Venv\Scripts\python.exe" set "PY_EXE=C:\Arena_Lab_V2_Venv\Scripts\python.exe"
if "%PY_EXE%"=="" if exist "%cd%\..\_Project_Archive_And_Offline_Tools\Offline_Tools\venv\Scripts\python.exe" set "PY_EXE=%cd%\..\_Project_Archive_And_Offline_Tools\Offline_Tools\venv\Scripts\python.exe"
if "%PY_EXE%"=="" set "PY_EXE=python"

if exist "Y:\logs" (
    "%PY_EXE%" "%cd%\Arena_Analytics.py" --remote Y:\ --auto-refresh
) else (
    "%PY_EXE%" "%cd%\Arena_Analytics.py"
)

set "ERR=%errorlevel%"
popd
if not "%ERR%"=="0" pause
exit /b %ERR%
