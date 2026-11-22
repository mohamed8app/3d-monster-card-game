@echo off
title Arena Lab V2 - PC #2 Headless Background Worker (Turn Screen OFF Safely!)
pushd "%~dp0"

echo ========================================================================
echo  ARENA LAB V2 - PC #2 HEADLESS TRAINING WORKER
echo  - Disables Sleep Mode on AC power so PC #2 keeps training with screen OFF
echo  - Auto-registers in Windows Startup so PC #2 reconnects after any reboot
echo  - Controlled 100%% remotely from Main PC (Arena_Trainer.py)
echo ========================================================================
echo.

:: 1. Prevent PC #2 from sleeping when monitor is turned off / closed
powercfg /change standby-timeout-ac 0 >nul 2>&1
powercfg /change hibernate-timeout-ac 0 >nul 2>&1

:: 2. Register in PC #2 Windows Startup folder & Desktop so user never has to open screen again
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ws = New-Object -ComObject WScript.Shell; " ^
  "$startup = [Environment]::GetFolderPath('Startup'); " ^
  "$desk = [Environment]::GetFolderPath('Desktop'); " ^
  "foreach ($dir in @($startup, $desk)) { " ^
  "  $s = $ws.CreateShortcut((Join-Path $dir 'Arena Lab V2 - PC2 Headless Worker.lnk')); " ^
  "  $s.TargetPath = '%~f0'; $s.WorkingDirectory = '%~dp0'; $s.Save(); " ^
  "}" >nul 2>&1

:: 3. Locate fast local Python venv first
set "PY_EXE="
if exist "C:\Arena_Lab_V2_Venv\Scripts\python.exe" set "PY_EXE=C:\Arena_Lab_V2_Venv\Scripts\python.exe"
if "%PY_EXE%"=="" if exist "%cd%\venv\Scripts\python.exe" set "PY_EXE=%cd%\venv\Scripts\python.exe"
if "%PY_EXE%"=="" if exist "%cd%\..\_Project_Archive_And_Offline_Tools\Offline_Tools\venv\Scripts\python.exe" set "PY_EXE=%cd%\..\_Project_Archive_And_Offline_Tools\Offline_Tools\venv\Scripts\python.exe"
if "%PY_EXE%"=="" set "PY_EXE=python"

:loop
"%PY_EXE%" "%cd%\pc2_headless_worker.py"
echo.
echo Worker exited (code %errorlevel%). Restarting in 5 seconds (Press Ctrl+C to quit)...
timeout /t 5
goto :loop
