@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo   Yarpa Support Agent - Installer
echo ============================================
echo.
echo This will install the Yarpa Support Agent on this computer
echo and register it as a background Windows service.
echo A Windows security prompt (UAC) will ask for Administrator
echo permission - please click Yes.
echo.

set "SITECODE="
set /p SITECODE="Site/customer code (optional, press Enter to skip): "

rem The extracted config file lands flat next to this script; put it back
rem under config\ so the agent finds it at its expected relative path.
if exist "%~dp0payment-terminal-vendors.json" (
    if not exist "%~dp0config" mkdir "%~dp0config"
    move /y "%~dp0payment-terminal-vendors.json" "%~dp0config\payment-terminal-vendors.json" >nul
)

if "%SITECODE%"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0install-agent.ps1\"'"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0install-agent.ps1\" -SiteCustomerCode \"%SITECODE%\"'"
)

echo.
echo Installation window closed. If a green "Installation complete" message
echo appeared in the elevated window, the agent is installed and running.
echo.
pause
