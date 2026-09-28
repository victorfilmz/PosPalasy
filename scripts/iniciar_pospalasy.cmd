@echo off
REM PosPalasy - Arranque diario con verificacion previa (doc 21 §7).
REM Valida variable de contrasena, certificado, BD y puerto; luego levanta la app.
REM Requiere que Certificado__Password este definida a nivel usuario (doc 21 §7.1).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0iniciar_pospalasy.ps1"
echo.
pause
