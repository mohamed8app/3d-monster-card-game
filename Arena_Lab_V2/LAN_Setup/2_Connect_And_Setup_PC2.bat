@echo off
title Arena Lab V2 - Step 2: Connect LAN Drive Z: & Setup Fast Local Python (PC #2)
setlocal enabledelayedexpansion

echo ========================================================================
echo  ARENA LAB V2 - TRAINING PC (PC #2) ONE-TIME SETUP
echo ========================================================================
echo.
set "HOST_IP=192.168.1.2"
set /p "INPUT_IP=Enter Host PC IP address [Press Enter for %HOST_IP%]: "
if not "%INPUT_IP%"=="" set "HOST_IP=%INPUT_IP%"

set "SHARE_UNC=\\%HOST_IP%\Arena_Lab_V2"
set "LAN_USER=%HOST_IP%\ArenaLAN"
set "LAN_PASS=Arena2026"

echo.
echo [1/4] Testing network & SMB Port 445 to %HOST_IP% ...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$t = Test-NetConnection -ComputerName '%HOST_IP%' -Port 445 -WarningAction SilentlyContinue; " ^
  "Write-Host ('  Ping OK: ' + $t.PingSucceeded + '  |  SMB Port 445 OK: ' + $t.TcpTestSucceeded)"

echo.
echo [2/4] Mapping %SHARE_UNC% to persistent drive Z: using dedicated LAN account...
net use Z: /delete /y >nul 2>&1
net use "%SHARE_UNC%" /delete /y >nul 2>&1
cmdkey /add:%HOST_IP% /user:%LAN_USER% /pass:%LAN_PASS% >nul 2>&1
net use Z: "%SHARE_UNC%" /user:%LAN_USER% %LAN_PASS% /persistent:yes
if errorlevel 1 (
    echo.
    echo ERROR: Could not map %SHARE_UNC% to Z:.
    echo Checklist:
    echo   1. Did you run LAN_Setup\1_Enable_LAN_Share_THIS_PC_RunAsAdmin.bat AS ADMINISTRATOR on Host PC?
    echo   2. Are BOTH PCs connected to the SAME router (same 192.168.1.x subnet, NOT Guest Wi-Fi)?
    echo   3. Check if third-party antivirus firewall on Host PC is blocking TCP port 445.
    pause
    exit /b 1
)

echo.
echo [3/4] Creating fast LOCAL Python CPU environment at C:\Arena_Lab_V2_Venv ...
if not exist "C:\Arena_Lab_V2_Venv\Scripts\python.exe" (
    python -m venv "C:\Arena_Lab_V2_Venv"
    if errorlevel 1 (
        echo ERROR: Python 3.10+ not found on PC #2. Please install Python 3.10+ with "Add Python to PATH" checked.
        pause
        exit /b 1
    )
)

"C:\Arena_Lab_V2_Venv\Scripts\python.exe" -m pip install --upgrade pip
"C:\Arena_Lab_V2_Venv\Scripts\python.exe" -m pip install numpy openpyxl
"C:\Arena_Lab_V2_Venv\Scripts\python.exe" -m pip install torch --index-url https://download.pytorch.org/whl/cpu
if errorlevel 1 (
    echo ERROR: Failed to install Python packages. Check internet connection on PC #2.
    pause
    exit /b 1
)

echo.
echo [4/4] Creating Desktop & Startup shortcuts on PC #2 (including Headless Worker)...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ws = New-Object -ComObject WScript.Shell; " ^
  "$desk = [Environment]::GetFolderPath('Desktop'); " ^
  "$startup = [Environment]::GetFolderPath('Startup'); " ^
  "$s1 = $ws.CreateShortcut((Join-Path $desk 'Arena Lab V2 - PC2 Headless Worker.lnk')); " ^
  "$s1.TargetPath = 'Z:\Start_PC2_Headless_Worker.bat'; $s1.WorkingDirectory = 'Z:\'; $s1.Save(); " ^
  "$s_st = $ws.CreateShortcut((Join-Path $startup 'Arena Lab V2 - PC2 Headless Worker.lnk')); " ^
  "$s_st.TargetPath = 'Z:\Start_PC2_Headless_Worker.bat'; $s_st.WorkingDirectory = 'Z:\'; $s_st.Save(); " ^
  "$s2 = $ws.CreateShortcut((Join-Path $desk 'Arena Lab V2 - Shared Folder (Z).lnk')); " ^
  "$s2.TargetPath = 'Z:\'; $s2.Save();"

echo.
echo ========================================================================
echo  ALL DONE ON PC #2!
echo  - Shared Drive Z: -> %SHARE_UNC%
echo  - Fast Local Python Venv -> C:\Arena_Lab_V2_Venv
echo  - Desktop Shortcut Created: "Arena Lab V2 - Trainer (LAN)"
echo ========================================================================
pause
