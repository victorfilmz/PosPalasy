@echo off
REM =====================================================================
REM PosPalasy - Recordatorio de renovacion del certificado digital
REM (doc 21). Lee la fecha de vencimiento REAL del .pfx activo, calcula
REM dias restantes y registra el resultado en DOS canales:
REM   - Log de PosPalasy: %LOCALAPPDATA%\\PosPalasy\\logs\\recordatorio-certificado-*.log
REM   - Visor de Eventos de Windows (origen PosPalasyCert):
REM       Id 100 = D-60 iniciar tramite
REM       Id 101 = D-30 certificado nuevo listo + ventana de corte
REM       Id 102 = D-15 verificacion en profundidad
REM       Id 103 = D-7  ultima barrera / escalar
REM       Id 200 = OK, sin accion (vence a mas de 60 dias)
REM       Id 201 = ERROR: certificado ausente o ilegible
REM
REM Mantenimiento: dentro de los bloques if/for NO puede haber parentesis en
REM los mensajes de eventcreate, el parser de cmd los cuenta como cierre de
REM bloque aunque esten entre comillas. Usar corchetes [ ].
REM La contraseña del certificado entra por la variable de entorno
REM POSPALASY_CERTPWD (nunca versionada), igual que la app la toma de
REM user-secrets/entorno. El script no imprime la contraseña en ningun
REM canal de salida.
REM =====================================================================

setlocal
set SCRIPTDIR=%~dp0
set PFX=%LOCALAPPDATA%\PosPalasy\certificados\emisor.pfx
set LOGDIR=%LOCALAPPDATA%\PosPalasy\logs

REM Log con nombre fijo: la fecha que guarda el mismo nombre es la del
REM ultimo run, no la de hoy. Eso mantiene el archivo en el visor de
REM Eventos y no rompe la comprobacion con findstr de mas de 9 digitos.
set LOG=%LOGDIR%\recordatorio-certificado.log

if not exist "%LOGDIR%" mkdir "%LOGDIR%" >nul 2>&1

echo [%date% %time%] Recordatorio certificado: inicio >> "%LOG%"

if not exist "%PFX%" (
  echo [%date% %time%] ERROR: no existe el certificado en %PFX% >> "%LOG%"
  eventcreate /T WARNING /ID 201 /L APPLICATION /SO PosPalasyCert /D "[Recordatorio certificado] NO existe el .pfx en %PFX%. Verificar instalacion, doc 16 punto 2." >nul 2>&1
  endlocal & exit /b 1
)
if "%POSPALASY_CERTPWD%"=="" (
  echo [%date% %time%] ERROR: la variable de entorno POSPALASY_CERTPWD con la contrasena del certificado no esta definida >> "%LOG%"
  eventcreate /T WARNING /ID 201 /L APPLICATION /SO PosPalasyCert /D "[Recordatorio certificado] Defina la variable de entorno POSPALASY_CERTPWD con la contrasena del certificado para que el recordatorio pueda leer su vencimiento." >nul 2>&1
  endlocal & exit /b 1
)

REM Fecha de vencimiento REAL del .pfx activo (NotAfter) y dias restantes,
REM en UNA sola llamada de PowerShell: una linea "VENCE;DIAS".
for /f "usebackq tokens=1,2 delims=;" %%A in (`powershell -NoProfile -Command "$c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('%PFX%', $env:POSPALASY_CERTPWD); Write-Output ($c.NotAfter.ToString('yyyy-MM-dd') + ';' + [int]($c.NotAfter - (Get-Date)).TotalDays)"`) do (
  set "VENCE=%%A"
  set "DIAS=%%B"
)

REM Validacion estricta: DIAS debe ser numerico, con signo opcional.
echo %DIAS%| findstr /r "^[0-9][0-9]*$ ^-[0-9][0-9]*$" >nul
if errorlevel 1 (
  echo [%date% %time%] ERROR: no se pudo leer el vencimiento del .pfx, DIAS=%DIAS% >> "%LOG%"
  eventcreate /T WARNING /ID 201 /L APPLICATION /SO PosPalasyCert /D "[Recordatorio certificado] No se pudo leer la fecha de vencimiento del .pfx. Verificar el archivo y la contrasena." >nul 2>&1
  endlocal & exit /b 1
)

echo [%date% %time%] Certificado vence %VENCE%, faltan %DIAS% dias >> "%LOG%"

if %DIAS% LEQ 7 (
  eventcreate /T ERROR /ID 103 /L APPLICATION /SO PosPalasyCert /D "[Ultima barrera] el certificado vence en %DIAS% dias, el %VENCE%. Escalar ahora: sin renovacion la DGII rechazara cada e-CF desde el vencimiento. Doc 21 fase D-7." >nul 2>&1
  echo [%date% %time%] EVENTO Id 103, D-7 >> "%LOG%"
) else if %DIAS% LEQ 15 (
  eventcreate /T ERROR /ID 102 /L APPLICATION /SO PosPalasyCert /D "[Verificacion en profundidad] certificado vence en %DIAS% dias, el %VENCE%. Confirmar venta de prueba con el nuevo certificado. Doc 21 fase D-15." >nul 2>&1
  echo [%date% %time%] EVENTO Id 102, D-15 >> "%LOG%"
) else if %DIAS% LEQ 30 (
  eventcreate /T WARNING /ID 101 /L APPLICATION /SO PosPalasyCert /D "[D-30] certificado vence en %DIAS% dias, el %VENCE%. El health check ahora reporta Degraded. Cargar el .pfx nuevo en la ventana de corte. Doc 21 fase D-30." >nul 2>&1
  echo [%date% %time%] EVENTO Id 101, D-30 >> "%LOG%"
) else if %DIAS% LEQ 60 (
  eventcreate /T INFORMATION /ID 100 /L APPLICATION /SO PosPalasyCert /D "[D-60] certificado vence en %DIAS% dias, el %VENCE%. INICIE el tramite de renovacion en la certificadora, doc 19. Doc 21 fase D-60." >nul 2>&1
  echo [%date% %time%] EVENTO Id 100, D-60 >> "%LOG%"
) else (
  eventcreate /T INFORMATION /ID 200 /L APPLICATION /SO PosPalasyCert /D "[OK] certificado vigente hasta %VENCE%, %DIAS% dias. Sin accion." >nul 2>&1
  echo [%date% %time%] OK, Id 200 >> "%LOG%"
)

echo [%date% %time%] Recordatorio certificado: fin >> "%LOG%"
endlocal
exit /b 0
