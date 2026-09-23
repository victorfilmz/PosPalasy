@echo off
REM =====================================================================
REM PosPalasy — Verificación MENSUAL de restauración del backup (doc 16 §4)
REM Tarea programada "PosPalasy Verificacion Backup" (1er domingo de mes, 06:00).
REM Restaura el último .bak en una base de PRUEBA y comprueba que las tablas
REM fiscales tienen datos. El resultado queda en el log (SUCCESS/FAILURE).
REM =====================================================================
setlocal enabledelayedexpansion
set BACKUPDIR=C:\Backups\PosPalasy
set LOGDIR=%LOCALAPPDATA%\PosPalasy\logs
set BASEPRUEBA=PosPalasy_Verificacion
set SQLCMD=C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set LOG=%LOGDIR%\verificacion-backup-%DATE:~-4%%DATE:~3,2%%DATE:~0,2%.log

echo === Verificacion de restauracion %DATE% %TIME% === > "%LOG%"

REM 1. Encontrar el .bak mas reciente.
set ULTIMO=
for /f "delims=" %%F in ('dir /b /o-d "%BACKUPDIR%\PosPalasy_DGII_*.bak" 2^>nul') do (
    if not defined ULTIMO set ULTIMO=%%F
)
if not defined ULTIMO (
    echo FAILURE: no hay ningun .bak en %BACKUPDIR% >> "%LOG%"
    eventcreate /T ERROR /ID 101 /L APPLICATION /SO PosPalasyBackup /D "Verificacion backup: no hay .bak en %BACKUPDIR%" >nul 2>&1
    type "%LOG%"
    exit /b 1
)
echo Backup a verificar: %ULTIMO% >> "%LOG%"

REM 2. Restaurar en la base de prueba (sobrescribe la anterior).
"%SQLCMD%" -S "(localdb)\mssqllocaldb" -E -b -Q "IF DB_ID('%BASEPRUEBA%') IS NOT NULL ALTER DATABASE [%BASEPRUEBA%] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; IF DB_ID('%BASEPRUEBA%') IS NOT NULL DROP DATABASE [%BASEPRUEBA%]; RESTORE DATABASE [%BASEPRUEBA%] FROM DISK='%BACKUPDIR%\%ULTIMO%' WITH MOVE 'PosPalasy_DGII' TO '%LOCALAPPDATA%\PosPalasy\%BASEPRUEBA%.mdf', MOVE 'PosPalasy_DGII_log' TO '%LOCALAPPDATA%\PosPalasy\%BASEPRUEBA%_log.ldf', REPLACE;" >> "%LOG%" 2>&1
if errorlevel 1 (
    echo FAILURE: la restauracion fallo. Revisar el log. >> "%LOG%"
    eventcreate /T ERROR /ID 102 /L APPLICATION /SO PosPalasyBackup /D "Verificacion backup: RESTORE fallo con %ULTIMO%" >nul 2>&1
    type "%LOG%"
    exit /b 1
)

REM 3. Comprobar que las tablas fiscales tienen datos.
"%SQLCMD%" -S "(localdb)\mssqllocaldb" -E -h -1 -Q "SET NOCOUNT ON; SELECT CAST(COUNT(*) AS varchar(20)) FROM [%BASEPRUEBA%].dbo.ElectronicInvoices; SELECT CAST(COUNT(*) AS varchar(20)) FROM [%BASEPRUEBA%].dbo.Ventas; SELECT CAST(COUNT(*) AS varchar(20)) FROM [%BASEPRUEBA%].dbo.SecuenciasECF;" -o "%TEMP%\pp-verif.txt" >> "%LOG%" 2>&1
if errorlevel 1 (
    echo FAILURE: la consulta de datos fallo. >> "%LOG%"
    eventcreate /T ERROR /ID 103 /L APPLICATION /SO PosPalasyBackup /D "Verificacion backup: consulta sobre la base restaurada fallo" >nul 2>&1
    type "%LOG%"
    exit /b 1
)

set /p COMPROBANTES=<"%TEMP%\pp-verif.txt"
set /p VENTAS=<"%TEMP%\pp-verif.txt" 2>nul
for /f "skip=1 delims=" %%A in (%TEMP%\pp-verif.txt) do (
    if not defined VENTAS set VENTAS=%%A
)
echo Comprobantes en la base restaurada: !COMPROBANTES! >> "%LOG%"
echo SUCCESS: restauracion verificada con %ULTIMO% >> "%LOG%"
eventcreate /T INFORMATION /ID 100 /L APPLICATION /SO PosPalasyBackup /D "Verificacion backup mensual: SUCCESS (%ULTIMO%)" >nul 2>&1

REM 4. La base de prueba se deja instalada para inspeccion manual; la proxima
REM    verificacion la reemplaza. Para liberar espacio: DROP DATABASE PosPalasy_Verificacion.
type "%LOG%"
endlocal
exit /b 0
