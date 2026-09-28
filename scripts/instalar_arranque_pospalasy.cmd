@echo off
REM =====================================================================
REM PosPalasy - Instalacion del arranque automatico (doc 21 §7).
REM Ejecutar UNA vez como administrador. Crea la tarea programada
REM "PosPalasy Arranque": al iniciar sesion, ejecuta el arranque con
REM verificacion previa (variable, certificado, BD, puerto) que levanta
REM la app solo si todo pasa. Corre oculto; el log queda en
REM %%LOCALAPPDATA%%\PosPalasy\logs\arranque-pospalasy.log
REM =====================================================================

setlocal
set SCRIPTDIR=%~dp0

echo Registrando la tarea de arranque automatico (al iniciar sesion)...
schtasks /Create /F /TN "PosPalasy Arranque" ^
  /TR "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"%SCRIPTDIR%iniciar_pospalasy.ps1\"" ^
  /SC ONLOGON /RL HIGHEST
if errorlevel 1 (
  echo ERROR: no se pudo crear la tarea. Ejecute este script como administrador.
  exit /b 1
)

echo.
echo Ejecutando la tarea una vez como prueba...
schtasks /Run /TN "PosPalasy Arranque"
if errorlevel 1 (
  echo AVISO: no se pudo ejecutar la tarea de prueba manualmente. Verificar en el Visor de tareas.
  exit /b 1
)

echo.
echo Esperando el ciclo de arranque - validaciones y health check - 90 s...
timeout /t 90 /nobreak >nul

powershell -NoProfile -Command "$s = $null; try { $s = Invoke-RestMethod -Uri 'http://localhost:5099/health' -TimeoutSec 5 } catch {}; if ($s) { Write-Output ('LISTO: /health = ' + $s) } else { Write-Output 'AVISO: /health no respondio; revisar arranque-pospalasy.log' }"

echo.
echo LISTO: PosPalasy arrancara automaticamente al iniciar sesion.
echo Canales de verificacion:
echo   - Log: %%LOCALAPPDATA%%\PosPalasy\logs\arranque-pospalasy.log
echo   - App:  %%LOCALAPPDATA%%\PosPalasy\logs\pospalasy-app.log / .err.log
echo   - /health en http://localhost:5099
endlocal
