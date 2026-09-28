# =====================================================================
# PosPalasy - Monitoreo semanal de las tareas programadas (doc 16 §4,
# doc 21 §4). Verifica que las 4 tareas de PosPalasy corrieron dentro
# de su ventana y que NO existan eventos Id 201 (error) del origen
# PosPalasyCert en los ultimos 7 dias. Deja el veredicto en el log y
# sale 0 (todo bien) o 1 (requiere atencion).
#
# Tareas vigiladas:
#   PosPalasy Arranque              (al iniciar sesion)
#   PosPalasy Backup Diario         (diaria 23:00)
#   PosPalasy Recordatorio Certif.  (diaria 08:00)
#   PosPalasy Verificacion Backup   (domingos 06:00)
# =====================================================================

$ErrorActionPreference = 'Continue'

$logDir = Join-Path $env:LOCALAPPDATA 'PosPalasy\logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
$log = Join-Path $logDir 'monitoreo-semanal.log'

function Log([string]$m) {
  $linea = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $m
  Write-Output $linea
  Add-Content -Path $log -Value $linea
}

Log "==== Monitoreo semanal de tareas PosPalasy ===="
$problemas = 0

# --- 1) Las 4 tareas: ultima ejecucion dentro de su ventana y resultado 0 ---
# Ventanas tolerantes (horas desde la ultima ejecucion antes de alertar):
$vigiladas = @(
  @{ Nombre = 'PosPalasy Backup Diario';         MaxHoras = 48 },  # diaria 23:00
  @{ Nombre = 'PosPalasy Recordatorio Certificado'; MaxHoras = 48 },  # diaria 08:00
  @{ Nombre = 'PosPalasy Verificacion Backup';   MaxHoras = 192 }, # semanal domingo
  @{ Nombre = 'PosPalasy Arranque';              MaxHoras = 0 }    # onlogon: solo resultado
)

foreach ($t in $vigiladas) {
  $info = Get-ScheduledTaskInfo -TaskName $t.Nombre -ErrorAction SilentlyContinue
  if (-not $info) {
    Log "FALLO: la tarea '$($t.Nombre)' NO existe. Reinstalar (doc 16 §4 / doc 21 §4)."
    $problemas++
    continue
  }
  $resultado = $info.LastTaskResult
  $ultima = $info.LastRunTime
  if ($resultado -eq 267011) {
    # 0x41303: la tarea aun no ha corrido (p. ej. su primera fecha programada es futura)
    Log "OK: '$($t.Nombre)' aun no tiene ejecuciones (proxima: $($info.NextRunTime))."
    continue
  }
  if ($ultima -eq [DateTime]::MinValue -or -not $ultima -or $ultima.Year -lt 2000) {
    Log "AVISO: '$($t.Nombre)' nunca ha corrido (proxima: $($info.NextRunTime))."
    if ($t.MaxHoras -gt 0) { $problemas++ }
    continue
  }
  $horas = ((Get-Date) - $ultima).TotalHours
  $estadoRes = if ($resultado -eq 0) { 'OK' } else { "codigo $resultado" }

  if ($t.MaxHoras -gt 0 -and $horas -gt $t.MaxHoras) {
    Log "FALLO: '$($t.Nombre)' ultima ejecucion hace $([int]$horas) h (>$($t.MaxHoras) h), resultado $estadoRes."
    $problemas++
  } elseif ($resultado -ne 0) {
    Log "AVISO: '$($t.Nombre)' corrio hace $([int]$horas) h pero termino con codigo $resultado."
    $problemas++
  } else {
    Log "OK: '$($t.Nombre)' corrio hace $([int]$horas) h, resultado 0."
  }
}

# --- 2) Eventos Id 201 (error) de PosPalasyCert en los ultimos 7 dias ---
$desde = (Get-Date).AddDays(-7)
try {
  $err = @(Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='PosPalasyCert'; Id=201; StartTime=$desde} -ErrorAction Stop)
} catch {
  $err = @()  # sin eventos = sin errores
}
if ($err.Count -gt 0) {
  Log "FALLO: $($_err.Count) evento(s) Id 201 (error del recordatorio) en los ultimos 7 dias:"
  foreach ($e in $err) {
    Log ("  {0:yyyy-MM-dd HH:mm} | {1}" -f $e.TimeCreated, ($e.Message -replace "`r`n", ' '))
  }
  Log "  Correccion: doc 21 §7.3 (variable POSPALASY_CERTPWD / .pfx)."
  $problemas++
} else {
  Log "OK: sin eventos Id 201 (error) en los ultimos 7 dias."
}

# --- 3) La app responde (la tarea de Arranque hizo su trabajo) ---
try {
  $salud = Invoke-RestMethod -Uri 'http://localhost:5099/health' -TimeoutSec 5
  Log "OK: la app responde /health = $salud."
} catch {
  Log "AVISO: la app no responde en http://localhost:5099 (arranque no ejecutado o caida). Revisar arranque-pospalasy.log."
}

if ($problemas -gt 0) {
  Log "==== VEREDICTO: REQUIERE ATENCION ($problemas problema(s)) ===="
  exit 1
}
Log "==== VEREDICTO: TODO CORRECTO ===="
exit 0
