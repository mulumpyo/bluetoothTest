@echo off
cd /d "%~dp0gui"
if exist "dist\Bluetooth-Adapter-Lab-1.1.2-x64.exe" (
  start "" "dist\Bluetooth-Adapter-Lab-1.1.2-x64.exe"
  exit /b
)
if not exist "node_modules\electron" (
  echo Install GUI dependencies with: cd gui ^&^& npm ci
  pause
  exit /b 1
)
call npm start
if errorlevel 1 pause
