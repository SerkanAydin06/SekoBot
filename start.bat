@echo off
setlocal
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
  echo [SekoBot] Python sanal ortami olusturuluyor...
  py -3 -m venv .venv
  if errorlevel 1 goto :error
)
".venv\Scripts\python.exe" -m pip install -e .
if errorlevel 1 goto :error
".venv\Scripts\python.exe" -m sekobot
if errorlevel 1 goto :error
exit /b 0
:error
echo.
echo SekoBot baslatilamadi. Python 3.11 veya ustunu kontrol edin.
pause
exit /b 1
