@echo off
"C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -quit -projectPath "%~dp0UnityGame" -executeMethod MonsterCardGame.Editor.BattleSceneBuilder.BuildWindowsExe -logFile "%~dp0build_fix.log"
exit /b %ERRORLEVEL%

