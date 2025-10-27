@echo off
REM 
setlocal
set PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe
set SCRIPT=%~dp0fetch-bubbles.ps1

"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
endlocal
