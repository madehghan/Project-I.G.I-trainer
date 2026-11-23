@echo off
chcp 65001 >nul
cd /d "%~dp0"
set CSC=
if exist "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if "%CSC%"=="" if exist "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if "%CSC%"=="" (
  echo csc.exe not found. Install .NET Framework 4.x Developer Pack / Windows has Framework 4.
  pause
  exit /b 1
)

"%CSC%" /nologo /optimize+ /target:winexe /platform:x86 /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0IgiTrainer.exe" "%~dp0IgiTrainer.cs"
if errorlevel 1 (
  echo Build FAILED
  pause
  exit /b 1
)
echo Built: IgiTrainer.exe
echo GitHub: https://github.com/madehghan/Project-I.G.I-trainer
exit /b 0
