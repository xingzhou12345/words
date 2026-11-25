@echo off
chcp 65001 >nul
echo Stopping IELTS word lookup helper...

taskkill /IM IeltsHotkey.exe /F >nul 2>&1
if %errorlevel%==0 (echo   stopped IeltsHotkey.exe) else (echo   IeltsHotkey.exe not running)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'powershell.exe' -and $_.CommandLine -like '*hotkey.ps1*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host ('  stopped old powershell pid ' + $_.ProcessId) }" 2>nul

echo Done.
timeout /t 3 >nul
