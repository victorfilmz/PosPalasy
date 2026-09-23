@echo off
REM =====================================================================
REM PosPalasy — Instalación de la verificación MENSUAL de backup (doc 16 §4)
REM Ejecutar UNA vez como administrador. Crea la tarea programada
REM "PosPalasy Verificacion Backup": 1er domingo de cada mes a las 06:00.
REM =====================================================================

setlocal
set SCRIPTDIR=%~dp0

echo Registrando la tarea programada mensual (1er domingo, 06:00)...
schtasks /Create /F /TN "PosPalasy Verificacion Backup" ^
  /TR "cmd /c \"%SCRIPTDIR%verificar_restauracion_backup.cmd\"" ^
  /SC MONTHLY /MO FIRST /D SUN /ST 06:00 /RL HIGHEST
if errorlevel 1 (
  echo ERROR: no se pudo crear la tarea. Ejecute este script como administrador.
  exit /b 1
)

echo Ejecutando una verificacion de prueba...
call "%SCRIPTDIR%verificar_restauracion_backup.cmd"
if errorlevel 1 (
  echo ERROR: la verificacion de prueba fallo. Revise el log.
  exit /b 1
)

echo.
echo LISTO: verificacion mensual instalada. Proxima: 1er domingo 06:00.
echo El resultado queda en %%LOCALAPPDATA%%\PosPalasy\logs\verificacion-backup-*.log
echo y tambien en el Visor de Eventos (origen PosPalasyBackup).
endlocal
