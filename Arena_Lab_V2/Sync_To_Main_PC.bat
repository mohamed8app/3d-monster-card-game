@echo off
title Arena Lab V2 - Sync Trained Models and Logs to Main PC
pushd "%~dp0"

set "HOST_SHARE=\\192.168.1.2\Arena_Lab_V2"
echo ========================================================================
echo  SYNCING TRAINED MODELS AND LOGS TO MAIN PC: %HOST_SHARE%
echo ========================================================================
echo.

if not exist "%HOST_SHARE%\data" goto :no_share

if exist "%cd%\saved_models" robocopy "%cd%\saved_models" "%HOST_SHARE%\saved_models" /E /XO /R:2 /W:1
if exist "%cd%\logs" robocopy "%cd%\logs" "%HOST_SHARE%\logs" /E /XO /R:2 /W:1

echo.
echo ========================================================================
echo  SYNC COMPLETE! You can now open Start_Analytics.bat on Main PC.
echo ========================================================================
popd
pause
exit /b 0

:no_share
echo ERROR: Cannot reach %HOST_SHARE%
echo Open %HOST_SHARE% in File Explorer once first - User: ArenaLAN / Pass: Arena2026
popd
pause
exit /b 1
