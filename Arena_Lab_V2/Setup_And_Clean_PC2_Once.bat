@echo off
title Arena Lab V2 - ONE-TIME PC #2 D: Drive Cleaner, Share Enabler & Pure-Speed Worker
setlocal enabledelayedexpansion

:: Auto-elevate to Administrator automatically if double-clicked normally!
net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Requesting Administrator privileges to enable LAN sharing ^& clean PC #2...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 0
)

echo ========================================================================
echo  ARENA LAB V2 - PC #2 (D: DRIVE) CLEAN, LAN SHARE ^& PURE-SPEED SETUP
echo  - 100%% Pure Local D: Drive Training Speed (ZERO background copying!)
echo  - Enables Read-Only Live Viewing from Main PC over LAN
echo  - Cleans old logs ^& old 41 models on PC #2's D: drive
echo ========================================================================
echo.

:: 1. Connect Z:\ to Main PC (\\192.168.1.2\Arena_Lab_V2) temporarily just to grab latest V2.2 code
set "HOST_IP=192.168.1.2"
set "HOST_SHARE=\\%HOST_IP%\Arena_Lab_V2"
if not exist "Z:\data" (
    cmdkey /add:%HOST_IP% /user:%HOST_IP%\ArenaLAN /pass:Arena2026 >nul 2>&1
    net use Z: "%HOST_SHARE%" /user:%HOST_IP%\ArenaLAN Arena2026 /persistent:yes >nul 2>&1
)
set "SRC_DIR=%~dp0"
if exist "Z:\data" set "SRC_DIR=Z:\"

:: 2. Auto-locate Arena_Lab_V2 on PC #2's D: drive
set "PC2_DIR="
if exist "D:\deck game\Arena_Lab_V2\Arena_Trainer.py" set "PC2_DIR=D:\deck game\Arena_Lab_V2"
if "%PC2_DIR%"=="" if exist "D:\Arena_Lab_V2\Arena_Trainer.py" set "PC2_DIR=D:\Arena_Lab_V2"
if "%PC2_DIR%"=="" (
    for /f "delims=" %%F in ('dir /s /b "D:\Arena_Trainer.py" 2^>nul') do (
        if "!PC2_DIR!"=="" set "PC2_DIR=%%~dpF"
    )
)
if not "%PC2_DIR%"=="" (
    if "%PC2_DIR:~-1%"=="\" set "PC2_DIR=%PC2_DIR:~0,-1%"
)
if "%PC2_DIR%"=="" set "PC2_DIR=D:\Arena_Lab_V2"

echo [1/5] Located PC #2 Simulator Folder on D: Drive:
echo       "%PC2_DIR%"
if not exist "%PC2_DIR%" mkdir "%PC2_DIR%"

:: 3. Update PC #2's D: folder with latest V2.2 code & cards from Main PC (one-time code update)
if /i not "%SRC_DIR%"=="%PC2_DIR%\" (
    echo [2/5] Updating code ^& V2.2 cards on "%PC2_DIR%"...
    robocopy "%SRC_DIR%data" "%PC2_DIR%\data" /E /R:2 /W:1 >nul
    robocopy "%SRC_DIR%engine" "%PC2_DIR%\engine" /E /R:2 /W:1 >nul
    robocopy "%SRC_DIR%trainer" "%PC2_DIR%\trainer" /E /R:2 /W:1 >nul
    robocopy "%SRC_DIR%LAN_Setup" "%PC2_DIR%\LAN_Setup" /E /R:2 /W:1 >nul
    robocopy "%SRC_DIR%saved_models\warm_start_base" "%PC2_DIR%\saved_models\warm_start_base" /E /R:2 /W:1 >nul
    robocopy "%SRC_DIR%." "%PC2_DIR%" *.py *.bat /R:2 /W:1 >nul
)

:: 4. Clean old logs & old 41 models on PC #2's D: drive!
set "PY_EXE="
if exist "C:\Arena_Lab_V2_Venv\Scripts\python.exe" set "PY_EXE=C:\Arena_Lab_V2_Venv\Scripts\python.exe"
if "%PY_EXE%"=="" if exist "%PC2_DIR%\venv\Scripts\python.exe" set "PY_EXE=%PC2_DIR%\venv\Scripts\python.exe"
if "%PY_EXE%"=="" if exist "%PC2_DIR%\..\_Project_Archive_And_Offline_Tools\Offline_Tools\venv\Scripts\python.exe" set "PY_EXE=%PC2_DIR%\..\_Project_Archive_And_Offline_Tools\Offline_Tools\venv\Scripts\python.exe"
if "%PY_EXE%"=="" set "PY_EXE=python"

echo [3/5] Cleaning old logs ^& old 41 models on "%PC2_DIR%" (Smart Reset V2.2)...
"%PY_EXE%" -c "import sys; sys.path.insert(0, r'%PC2_DIR%'); from pc2_headless_worker import perform_smart_v22_reset; print('  CLEANED PC #2 D: ->', perform_smart_v22_reset(r'%PC2_DIR%'))"

:: 5. Enable SMB File Share on PC #2's D: folder (\\<PC2_IP>\Arena_Lab_V2) so Main PC can view live without copying!
echo [4/5] Enabling LAN Share on "%PC2_DIR%" (ArenaLAN / Arena2026 + Firewall 445)...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -ne 'DomainAuthenticated' } | Set-NetConnectionProfile -NetworkCategory Private -ErrorAction SilentlyContinue; " ^
  "Remove-NetFirewallRule -DisplayName 'Arena Lab V2 SMB (TCP 445)' -ErrorAction SilentlyContinue; " ^
  "New-NetFirewallRule -DisplayName 'Arena Lab V2 SMB (TCP 445)' -Direction Inbound -Protocol TCP -LocalPort 445 -Action Allow -Profile Any | Out-Null; " ^
  "Enable-NetFirewallRule -Name 'FPS-SMB-In-TCP','FPS-ICMP4-ERQ-In' -ErrorAction SilentlyContinue; " ^
  "Start-Service -Name LanmanServer -ErrorAction SilentlyContinue; " ^
  "$sec = ConvertTo-SecureString 'Arena2026' -AsPlainText -Force; " ^
  "$u = Get-LocalUser -Name 'ArenaLAN' -ErrorAction SilentlyContinue; " ^
  "if ($null -eq $u) { New-LocalUser -Name 'ArenaLAN' -Password $sec -PasswordNeverExpires -UserMayNotChangePassword | Out-Null } else { Set-LocalUser -Name 'ArenaLAN' -Password $sec -PasswordNeverExpires $true; Enable-LocalUser -Name 'ArenaLAN' }; " ^
  "Add-LocalGroupMember -Group 'Users' -Member 'ArenaLAN' -ErrorAction SilentlyContinue"
icacls "%PC2_DIR%" /grant "ArenaLAN:(OI)(CI)M" /grant "Users:(OI)(CI)M" /T /C /Q >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "if (Get-SmbShare -Name 'Arena_Lab_V2' -ErrorAction SilentlyContinue) { Remove-SmbShare -Name 'Arena_Lab_V2' -Force }; " ^
  "New-SmbShare -Name 'Arena_Lab_V2' -Path '%PC2_DIR%' -FullAccess 'ArenaLAN','%USERNAME%','Administrators','Everyone' | Out-Null"

:: 6. Disable Sleep & start Pure-Speed Headless Worker on PC #2's D: drive!
echo [5/5] Disabling Sleep ^& launching Pure-Speed Headless Worker on "%PC2_DIR%"...
powercfg /change standby-timeout-ac 0 >nul 2>&1
powercfg /change hibernate-timeout-ac 0 >nul 2>&1

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ws = New-Object -ComObject WScript.Shell; " ^
  "$startup = [Environment]::GetFolderPath('Startup'); " ^
  "$desk = [Environment]::GetFolderPath('Desktop'); " ^
  "foreach ($dir in @($startup, $desk)) { " ^
  "  $s = $ws.CreateShortcut((Join-Path $dir 'Arena Lab V2 - PC2 Headless Worker.lnk')); " ^
  "  $s.TargetPath = '%PC2_DIR%\Start_PC2_Headless_Worker.bat'; $s.WorkingDirectory = '%PC2_DIR%'; $s.Save(); " ^
  "}" >nul 2>&1

cd /d "%PC2_DIR%"
call "%PC2_DIR%\Start_PC2_Headless_Worker.bat"
