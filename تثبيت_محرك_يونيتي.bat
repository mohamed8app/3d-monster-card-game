@echo off
chcp 65001 > nul
title تثبيت محرك Unity 6
echo ========================================================
echo  جاري تشغيل مثبت محرك Unity 6 الذي تم تحميله مسبقاً...
echo ========================================================
echo.
echo تنبيه مهم:
echo عندما يسألك البرنامج عن مكان التثبيت (Destination Folder):
echo تأكد أن المسار هو: D:\Unity\Editor\6000.6.7f1
echo (حتى لا يتم التثبيت على القرص C).
echo.
start "" "D:\Unity\Downloads\UnitySetup64-6000.6.7f1.exe"
