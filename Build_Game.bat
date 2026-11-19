@echo off
chcp 65001 > nul
echo ======================================================================
echo           🎮 Monster Card Game - Unity 6 Build Script
echo ======================================================================
echo.
echo Building Game_Windows_Exe from UnityGame project...
echo Please wait (~30 seconds)...
echo.

set UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe
set PROJECT_PATH=D:\deck game\UnityGame
set LOG_FILE=D:\deck game\_Project_Archive_And_Offline_Tools\Backups_And_Logs\unity_build.log

"%UNITY_EXE%" -quit -batchmode -projectPath "%PROJECT_PATH%" -executeMethod MonsterCardGame.Editor.BattleSceneBuilder.BuildWindowsExe -logFile "%LOG_FILE%"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ======================================================================
    echo [SUCCESS] Game built successfully with 0 errors!
    echo Executable path: D:\deck game\Game_Windows_Exe\MonsterCardGame.exe
    echo ======================================================================
) else (
    echo.
    echo ======================================================================
    echo [ERROR] Build failed with exit code: %ERRORLEVEL%
    echo Check log details at: %LOG_FILE%
    echo ======================================================================
)
echo.
pause
