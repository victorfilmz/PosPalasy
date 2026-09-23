@echo off
REM =====================================================================
REM PosPalasy — Instalación del backup diario (doc 16 §4)
REM Ejecutar UNA vez como administrador. Crea:
REM   1. C:\Backups\PosPalasy (destino de los .bak)
REM   2. Tarea programada "PosPalasy Backup Diario" a las 23:00
REM   3. Limpieza de .bak con más de 30 días (misma tarea, segundo paso)
REM =====================================================================

setlocal
set BACKUPDIR=C:\Backups\PosPalasy
set SCRIPTDIR=%~dp0
set SQLCMD=sqlcmd

echo [1/3] Creando directorio de backups: %BACKUPDIR%
if not exist "%BACKUPDIR%" mkdir "%BACKUPDIR%"

echo [2/3] Registrando la tarea programada diaria (23:00)...
schtasks /Create /F /TN "PosPalasy Backup Diario" ^
  /TR "cmd /c %SQLCMD% -S (localdb)\mssqllocaldb -E -i \"%SCRIPTDIR%backup_pospalasy.sql\" && forfiles /P \"%BACKUPDIR%\" /M *.bak /D -30 /C \"cmd /c del @path\"" ^
  /SC DAILY /ST 23:00 /RL HIGHEST
if errorlevel 1 (
  echo ERROR: no se pudo crear la tarea. Ejecute este script como administrador.
  exit /b 1
)

echo [3/3] Ejecutando un backup de prueba...
%SQLCMD% -S "(localdb)\mssqllocaldb" -E -i "%SCRIPTDIR%backup_pospalasy.sql"
if errorlevel 1 (
  echo ERROR: el backup de prueba fallo. Revise que sqlcmd este en PATH y la base exista.
  exit /b 1
)

echo.
echo LISTO: backup diario instalado. Verifique el .bak en %BACKUPDIR%
echo Verificacion mensual obligatoria (doc 16): restaurar el ultimo .bak en una base de prueba.
endlocal
