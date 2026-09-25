@echo off
REM =====================================================================
REM PosPalasy - Instalacion del recordatorio de renovacion del
REM certificado digital (doc 21 §4). Ejecutar UNA vez como administrador.
REM Crea la tarea programada "PosPalasy Recordatorio Certificado":
REM diaria a las 08:00. Al instalar corre una ejecucion de prueba.
REM =====================================================================

setlocal
set SCRIPTDIR=%~dp0

echo Registrando la tarea programada diaria (08:00)...
schtasks /Create /F /TN "PosPalasy Recordatorio Certificado" ^
  /TR "cmd /c \"%SCRIPTDIR%recordatorio_certificado.cmd\"" ^
  /SC DAILY /ST 08:00 /RL HIGHEST
if errorlevel 1 (
  echo ERROR: no se pudo crear la tarea. Ejecute este script como administrador.
  exit /b 1
)

echo Ejecutando un recordatorio de prueba...
call "%SCRIPTDIR%recordatorio_certificado.cmd"
if errorlevel 1 (
  echo AVISO: la prueba retorno error (posible: .pfx ausente). La tarea quedo instalada.
  exit /b 1
)

echo.
echo LISTO: recordatorio diario instalado (08:00).
echo Canales de alerta:
echo   - Log: %%LOCALAPPDATA%%\PosPalasy\logs\recordatorio-certificado-*.log
echo   - Visor de Eventos: origen PosPalasyCert (Id 100/101/102/103 accion, 200 OK, 201 error)
endlocal
