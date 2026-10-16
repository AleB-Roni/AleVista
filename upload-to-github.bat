@echo off
chcp 65001 >nul
cd /d "%~dp0"
set REMOTE_URL=https://github.com/AleB-Roni/AleVista.git
echo === AleVista -^> GitHub ===
where git >nul 2>nul || (echo Git ist nicht installiert: https://git-scm.com & pause & exit /b 1)
if not exist .git git init -q -b main
git remote get-url origin >nul 2>nul || git remote add origin %REMOTE_URL%
git add -A
git diff --cached --quiet || git commit -q -m "Update %date% %time%"
git push -u origin main || (echo Push fehlgeschlagen - existiert das Repo? & pause & exit /b 1)
echo.
set /p VERSION=Als Release veroeffentlichen? Version (z. B. v1.0.0) oder Enter: 
if not "%VERSION%"=="" (git tag -a %VERSION% -m "AleVista %VERSION%" && git push origin %VERSION%)
echo Fertig.
pause
