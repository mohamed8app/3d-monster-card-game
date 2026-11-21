@echo off
title Arena Lab V2 - Step 1: Enable LAN Shared Folder (Run As Administrator)
setlocal enabledelayedexpansion

:: Check Administrator elevation cleanly
net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo ========================================================================
    echo  ADMINISTRATOR PRIVILEGES REQUIRED
    echo  Please RIGHT-CLICK this file and choose: "Run as administrator"
    echo ========================================================================
    pause
    exit /b 1
)

set "SHARE_NAME=Arena_Lab_V2"
set "LAN_USER=ArenaLAN"
set "LAN_PASS=Arena2026"
for %%I in ("%~dp0..") do set "SHARE_PATH=%%~fI"

echo ========================================================================
echo  ARENA LAB V2 - LAN SHARED FOLDER SETUP (HOST PC)
echo  Target Folder : %SHARE_PATH%
echo  Share Name    : %SHARE_NAME%
echo  LAN Account   : %LAN_USER%  (Dedicated account for PC #2 SMB access)
echo ========================================================================
echo.

:: 1. Ensure data & shared subfolders exist
if not exist "%SHARE_PATH%\saved_models" mkdir "%SHARE_PATH%\saved_models"
if not exist "%SHARE_PATH%\logs" mkdir "%SHARE_PATH%\logs"
if not exist "%SHARE_PATH%\Shared_Files" mkdir "%SHARE_PATH%\Shared_Files"

:: 2. Switch active LAN network profile to Private
echo [1/5] Setting active network profile to Private...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -ne 'DomainAuthenticated' } | Set-NetConnectionProfile -NetworkCategory Private; " ^
  "Get-NetConnectionProfile | Select-Object Name, InterfaceAlias, NetworkCategory | Format-Table -AutoSize"

:: 3. Open explicit Firewall rules for SMB (TCP 445) & Ping (ICMPv4) on Private profile (language-independent!)
echo [2/5] Opening Windows Firewall ports TCP 445 (SMB) and Ping on Private profile...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Remove-NetFirewallRule -DisplayName 'Arena Lab V2 SMB (TCP 445)' -ErrorAction SilentlyContinue; " ^
  "Remove-NetFirewallRule -DisplayName 'Arena Lab V2 Ping (ICMPv4)' -ErrorAction SilentlyContinue; " ^
  "New-NetFirewallRule -DisplayName 'Arena Lab V2 SMB (TCP 445)' -Direction Inbound -Protocol TCP -LocalPort 445 -Action Allow -Profile Private,Domain | Out-Null; " ^
  "New-NetFirewallRule -DisplayName 'Arena Lab V2 Ping (ICMPv4)' -Direction Inbound -Protocol ICMPv4 -IcmpType 8 -Action Allow -Profile Private,Domain | Out-Null; " ^
  "Enable-NetFirewallRule -Name 'FPS-SMB-In-TCP','FPS-ICMP4-ERQ-In' -ErrorAction SilentlyContinue; " ^
  "Start-Service -Name LanmanServer -ErrorAction SilentlyContinue; " ^
  "Set-Service -Name LanmanServer -StartupType Automatic -ErrorAction SilentlyContinue; " ^
  "Write-Host '  Firewall TCP 445 + Ping rules ACTIVE.' -ForegroundColor Green"

:: 4. Create/update dedicated non-blank-password local share account (required by Windows 10/11 SMB!)
echo [3/5] Configuring dedicated LAN share user '%LAN_USER%' (avoids blank-password & Win11 Guest blocks)...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$sec = ConvertTo-SecureString '%LAN_PASS%' -AsPlainText -Force; " ^
  "$u = Get-LocalUser -Name '%LAN_USER%' -ErrorAction SilentlyContinue; " ^
  "if ($null -eq $u) { " ^
  "  New-LocalUser -Name '%LAN_USER%' -Password $sec -FullName 'Arena Lab LAN Share' -Description 'Dedicated SMB account for Arena Lab V2' -PasswordNeverExpires -UserMayNotChangePassword | Out-Null; " ^
  "} else { " ^
  "  Set-LocalUser -Name '%LAN_USER%' -Password $sec -PasswordNeverExpires $true; " ^
  "  Enable-LocalUser -Name '%LAN_USER%'; " ^
  "} " ^
  "Add-LocalGroupMember -Group 'Users' -Member '%LAN_USER%' -ErrorAction SilentlyContinue; " ^
  "Write-Host '  User %LAN_USER% ready.' -ForegroundColor Green"

:: 5. Grant NTFS Read/Write ONLY on this folder + create SMB Share
echo [4/5] Applying folder permissions on %SHARE_PATH% ...
icacls "%SHARE_PATH%" /grant "%LAN_USER%:(OI)(CI)M" /grant "Users:(OI)(CI)M" /grant "Authenticated Users:(OI)(CI)M" /T /C /Q >nul

echo [5/5] Creating SMB Share '%SHARE_NAME%' and running local self-test...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "if (Get-SmbShare -Name '%SHARE_NAME%' -ErrorAction SilentlyContinue) { Remove-SmbShare -Name '%SHARE_NAME%' -Force }; " ^
  "New-SmbShare -Name '%SHARE_NAME%' -Path '%SHARE_PATH%' -FullAccess '%LAN_USER%','%USERNAME%','Administrators' -Description 'Arena Lab V2 Shared Training Folder' | Format-Table Name, Path -AutoSize; " ^
  "$ips = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' }).IPAddress; " ^
  "foreach ($ip in $ips) { " ^
  "  $t = Test-NetConnection -ComputerName $ip -Port 445 -WarningAction SilentlyContinue; " ^
  "  Write-Host ('  Self-Test TCP 445 on ' + $ip + ' : ' + $t.TcpTestSucceeded) -ForegroundColor Cyan; " ^
  "}"

echo.
echo ========================================================================
echo  READY! USE THESE EXACT DETAILS ON PC #2:
echo ========================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ips = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' }).IPAddress; " ^
  "foreach ($ip in $ips) { Write-Host ('  Network Path :  \\' + $ip + '\%SHARE_NAME%') -ForegroundColor Green }"
echo   Username     :  %COMPUTERNAME%\%LAN_USER%   (or simply: %LAN_USER%)
echo   Password     :  %LAN_PASS%
echo ========================================================================
pause
