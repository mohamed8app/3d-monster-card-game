@echo off
title Arena Lab V2 - Tkinter Analytics Viewer GUI
pushd "%~dp0"

set "PY_EXE="
if exist "%cd%\venv\Scripts\python.exe" set "PY_EXE=%cd%\venv\Scripts\python.exe"
if "%PY_EXE%"=="" if exist "C:\Arena_Lab_V2_Venv\Scripts\python.exe" set "PY_EXE=C:\Arena_Lab_V2_Venv\Scripts\python.exe"
if "%PY_EXE%"=="" if exist "%cd%\..\_Project_Archive_And_Offline_Tools\Offline_Tools\venv\Scripts\python.exe" set "PY_EXE=%cd%\..\_Project_Archive_And_Offline_Tools\Offline_Tools\venv\Scripts\python.exe"
if "%PY_EXE%"=="" set "PY_EXE=python"

"%PY_EXE%" -c "import numpy, tkinter" >nul 2>&1
if errorlevel 1 goto :need_install

echo Launching Arena Lab V2 Analytics Viewer Tkinter GUI...
"%PY_EXE%" "%cd%\Arena_Analytics.py"
set "ERR=%errorlevel%"
popd
if not "%ERR%"=="0" pause
exit /b %ERR%

:need_install
echo ========================================================================
echo  Required Python packages are not installed yet!
echo  Please double-click:  Install_Python_Packages.bat  first.
echo ========================================================================
popd
pause
exit /b 1
