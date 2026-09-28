@echo off
REM =====================================================================
REM PosPalasy - Instalacion del monitoreo semanal (doc 16 §4, doc 21 §4).
REM Ejecutar UNA vez como administrador. Crea la tarea programada
REM "PosPalasy Monitoreo Semanal": domingos 06:30 (despues de la
REM verificacion de backup de 06:00). Corre una vez como prueba.
REM =====================================================================

setlocal
set SCRIPTDIR=%~dp0

echo Registrando la tarea de monitoreo semanal (domingos 06:30)...
schtasks /Create /F /TN "PosPalasy Monitoreo Semanal" ^
  /TR "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"%SCRIPTDIR%monitor_semanal_pospalasy.ps1\"" ^
  /SC WEEKLY /D SUN /ST 06:30 /RL HIGHEST
if errorlevel 1 (
  echo ERROR: no se pudo crear la tarea. Ejecute este script como administrador.
  exit /b 1
)

echo Ejecutando el monitoreo una vez como prueba...
schtasks /Run /TN "PosPalasy Monitoreo Semanal"
if errorlevel 1 (
  echo AVISO: no se pudo ejecutar la prueba manual. Verificar en el Programador de tareas.
  exit /b 1
)

echo Esperando 20 s para el run de prueba...
timeout /t 20 /nobreak >nul

echo.
echo LISTO: monitoreo semanal instalado (domingos 06:30).
echo Canales de verificacion:
echo   - Log: %%LOCALAPPDATA%%\PosPalasy\logs\monitoreo-semanal.log
echo   - Exit code de la tarea: 0 = todo correcto, 1 = requiere atencion
endlocal
