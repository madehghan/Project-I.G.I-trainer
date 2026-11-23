@echo off
chcp 65001 >nul
title Project I.G.I Trainer — https://github.com/madehghan/igi
cd /d "%~dp0"

echo ============================================
echo  Project I.G.I Trainer (free)
echo  https://github.com/madehghan/igi
echo ============================================
echo.
echo  EN: Put this folder's files next to IGI.exe, then run this.
echo  FA: فایل‌ها را کنار IGI.exe بگذارید، بعد همین را اجرا کنید.
echo.

if not exist "%~dp0IgiTrainer.exe" (
  echo IgiTrainer.exe missing. Building...
  call "%~dp0BUILD.bat"
  if not exist "%~dp0IgiTrainer.exe" (
    echo Build failed. Install .NET Framework 4.x and try again.
    pause
    exit /b 1
  )
)

start "" "%~dp0IgiTrainer.exe"
echo Trainer started. Click Play inside the window.
echo ترینر باز شد — داخل پنجره Play را بزنید.
exit /b 0
