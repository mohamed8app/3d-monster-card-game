@echo off
title Arena Lab V2 - Connect Main PC to PC #2 D: Drive Share (Drive Y:)
setlocal enabledelayedexpansion

set "SILENT_MODE=0"
if /i "%~1"=="--silent" set "SILENT_MODE=1"

:: Fast path: If Drive Y: is already connected to PC #2's D: share, open immediately!
if exist "Y:\logs" (
    if "%SILENT_MODE%"=="0" (
        start "" explorer.exe "Y:\"
    )
    exit /b 0
)

:: 1. Check if PC #2 announced its exact IP in logs\pc2_host_info.json
set "PC2_IP="
for /f "usebackq delims=" %%A in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = Join-Path '%~dp0..' 'logs\pc2_host_info.json'; if (Test-Path $p) { try { $j = Get-Content $p -Raw | ConvertFrom-Json; if ($j.ip) { Write-Output $j.ip } } catch {} }"`) do (
    set "PC2_IP=%%A"
)

:: 2. Otherwise parallel-scan LAN port 445 in 0.4s
if "%PC2_IP%"=="" (
    for /f "usebackq delims=" %%A in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$myIps = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -like '192.168.*' }).IPAddress; $prefix = if ($myIps) { ($myIps[0] -split '\.')[0..2] -join '.' } else { '192.168.1' }; $tasks = @(); 1..254 | ForEach-Object { $ip = \"$prefix.$_\"; if ($myIps -contains $ip) { return }; try { $t = New-Object System.Net.Sockets.TcpClient; $ar = $t.BeginConnect($ip, 445, $null, $null); $tasks += ,@($ip, $t, $ar) } catch {} }; Start-Sleep -Milliseconds 400; $found = $null; foreach ($item in $tasks) { if (-not $found -and $item[1].Connected) { $found = $item[0] }; try { $item[1].Close() } catch {} }; if ($found) { Write-Output $found }"`) do (
        set "PC2_IP=%%A"
    )
)

if "%PC2_IP%"=="" (
    if "%SILENT_MODE%"=="1" exit /b 1
    echo ========================================================================
    echo  PC #2 SHARE NOT ENABLED YET!
    echo ========================================================================
    echo  To clean PC #2's D: folder and enable zero-copy live viewing:
    echo    1. Go to PC #2 and open Z:\  ^(or \\192.168.1.2\Arena_Lab_V2^)
    echo    2. Double-click:  Setup_And_Clean_PC2_Once.bat
    echo    3. Click "Yes" on the Administrator prompt, then turn off PC #2's screen!
    echo ========================================================================
    pause
    exit /b 1
)

set "SHARE_UNC=\\%PC2_IP%\Arena_Lab_V2"
set "LAN_USER=%PC2_IP%\ArenaLAN"
set "LAN_PASS=Arena2026"

net use Y: /delete /y >nul 2>&1
net use "%SHARE_UNC%" /delete /y >nul 2>&1
cmdkey /add:%PC2_IP% /user:%LAN_USER% /pass:%LAN_PASS% >nul 2>&1
net use Y: "%SHARE_UNC%" /user:%LAN_USER% %LAN_PASS% /persistent:yes >nul 2>&1

if exist "Y:\logs" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "$cfgPath = Join-Path '%~dp0..' 'logs\analytics_config.json'; " ^
      "$cfg = @{ mode = 'remote'; remote_dir = 'Y:\'; auto_refresh = $true }; " ^
      "$cfg | ConvertTo-Json | Set-Content -Path $cfgPath -Encoding UTF8"
    if "%SILENT_MODE%"=="0" (
        start "" explorer.exe "Y:\"
    )
    exit /b 0
)

if "%SILENT_MODE%"=="0" (
    echo Could not connect to %SHARE_UNC% yet. Run Setup_And_Clean_PC2_Once.bat on PC #2 first.
    pause
)
exit /b 1
