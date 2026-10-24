@echo off
setlocal
cd /d "%~dp0"
chcp 65001 >nul
title SanJing

if not exist "bin\Release\net8.0-windows\win-x64\SanJing.exe" (
  dotnet build -c Release
  if errorlevel 1 goto :error
)

start "" "bin\Release\net8.0-windows\win-x64\SanJing.exe" %*
exit /b 0

:error
echo Build failed. Install .NET 8 SDK and try again.
pause
exit /b 1
