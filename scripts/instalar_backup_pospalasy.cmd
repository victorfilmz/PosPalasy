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
REM Ruta absoluta: el perfil de la tarea programada (SYSTEM) puede no tener sqlcmd en PATH.
set SQLCMD=C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE

echo [1/3] Creando directorio de backups: %BACKUPDIR%
if not exist "%BACKUPDIR%" mkdir "%BACKUPDIR%"

echo [2/3] Registrando la tarea programada diaria (23:00)...
schtasks /Create /F /TN "PosPalasy Backup Diario" ^
  /TR "cmd /c \"%SCRIPTDIR%ejecutar_backup_pospalasy.cmd\"" ^
  /SC DAILY /ST 23:00 /RL HIGHEST
if errorlevel 1 (
  echo ERROR: no se pudo crear la tarea. Ejecute este script como administrador.
  exit /b 1
)

echo [3/3] Ejecutando un backup de prueba (por el wrapper de la tarea)...
call "%SCRIPTDIR%ejecutar_backup_pospalasy.cmd"
if errorlevel 1 (
  echo ERROR: el backup de prueba fallo. Revise que sqlcmd este en PATH y la base exista.
  exit /b 1
)

echo.
echo LISTO: backup diario instalado. Verifique el .bak en %BACKUPDIR%
echo Verificacion mensual obligatoria (doc 16): restaurar el ultimo .bak en una base de prueba.
endlocal
