@echo off
REM =====================================================================
REM PosPalasy — Backup diario de la base fiscal (doc 16 §4)
REM Tarea programada "PosPalasy Backup Diario": llama a este wrapper.
REM La lógica vive aquí (no en /TR de schtasks, limitado a 261 caracteres).
REM =====================================================================
setlocal
set BACKUPDIR=C:\Backups\PosPalasy
set SCRIPTDIR=%~dp0
set SQLCMD=C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE

"%SQLCMD%" -S "(localdb)\mssqllocaldb" -E -i "%SCRIPTDIR%backup_pospalasy.sql"

REM Retención: eliminar los .bak con más de 30 días.
forfiles /P "%BACKUPDIR%" /M *.bak /D -30 /C "cmd /c del @path" 2>nul
endlocal
