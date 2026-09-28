@echo off
REM PosPalasy - Monitoreo semanal de las tareas programadas y eventos Id 201.
REM Se ejecuta automaticamente por la tarea "PosPalasy Monitoreo Semanal"
REM (domingos 06:30, tras la verificacion de backup de 06:00). Tambien puede
REM correrse a mano: scripts\monitor_semanal_pospalasy.cmd
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0monitor_semanal_pospalasy.ps1"
