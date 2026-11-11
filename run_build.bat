@echo off
"C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -projectPath "%~dp0UnityGame" -executeMethod MonsterCardGame.Editor.BattleSceneBuilder.BuildWindowsExe -logFile "%~dp0UnityGame\Logs\build_windows.log"
exit /b %ERRORLEVEL%

