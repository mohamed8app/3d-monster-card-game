@echo off
title Arena Lab V2 - LAN Connection Diagnostic
set "HOST_IP=192.168.1.2"
set /p "INPUT_IP=Enter Host PC IP [Press Enter for %HOST_IP%]: "
if not "%INPUT_IP%"=="" set "HOST_IP=%INPUT_IP%"

echo.
echo --- 1. Ping Test ---
ping -n 2 %HOST_IP%

echo.
echo --- 2. SMB Port 445 Test ---
powershell -NoProfile -ExecutionPolicy Bypass -Command "Test-NetConnection -ComputerName '%HOST_IP%' -Port 445 | Select-Object ComputerName, RemotePort, TcpTestSucceeded | Format-Table -AutoSize"

echo.
echo --- 3. Share Read/Write Test ---
set "TEST_FILE=\\%HOST_IP%\Arena_Lab_V2\Shared_Files\_lan_write_test.txt"
echo LAN_OK_%DATE%_%TIME% > "%TEST_FILE%" 2>nul
if exist "%TEST_FILE%" (
    echo [PASS] Successfully wrote & verified file on \\%HOST_IP%\Arena_Lab_V2\Shared_Files !
    del "%TEST_FILE%" >nul 2>&1
) else (
    echo [FAIL] Cannot write to \\%HOST_IP%\Arena_Lab_V2\Shared_Files .
    echo        Make sure Step 1 ran as Administrator on Host PC and Drive Z: is signed in.
)
echo.
pause
