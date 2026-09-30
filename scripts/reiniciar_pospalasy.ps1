# =====================================================================
# PosPalasy - Reinicio seguro de la app de producción (:5099).
#
# ¿Por qué existe? La app exige ASPNETCORE_ENVIRONMENT=Development mientras
# DGII:ModoSimulador=true (Program.cs aborta el arranque si no). El script
# de arranque diario ya fija esa variable; este script hace lo mismo para
# los REINICIOS manuales (p. ej. tras recompilar), que es donde se olvida.
#
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File reiniciar_pospalasy.ps1
#      powershell ... -File reiniciar_pospalasy.ps1 -SinCompilar   (solo reinicia)
# =====================================================================

param(
  [switch]$SinCompilar
)

$ErrorActionPreference = 'Stop'

$repo   = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $repo 'src\POS.UI\POS.UI.csproj'
$exe    = Join-Path $repo 'src\POS.UI\bin\Debug\net10.0\POS.UI.exe'
$url    = 'http://localhost:5099'
$puerto = 5099
$logDir = Join-Path $env:LOCALAPPDATA 'PosPalasy\logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
$log = Join-Path $logDir 'reinicio-pospalasy.log'

function Log([string]$m) {
  $linea = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $m
  Write-Host $linea
  Add-Content -Path $log -Value $linea
}

Log "==== Reinicio de PosPalasy ===="

# --- 1) Detener la instancia actual si la hay ---
$ocupado = Get-NetTCPConnection -LocalPort $puerto -State Listen -ErrorAction SilentlyContinue
if ($ocupado) {
  $pidApp = $ocupado[0].OwningProcess
  Log "Deteniendo instancia actual (PID $pidApp)..."
  try { Stop-Process -Id $pidApp -Force -ErrorAction Stop; Start-Sleep -Seconds 2 }
  catch { Log "FALLO: no se pudo detener el PID $pidApp : $($_.Exception.Message)"; exit 1 }
} else {
  Log "No hay instancia corriendo en $puerto."
}

# --- 2) Compilar (opcional) ---
if (-not $SinCompilar) {
  Log "Compilando POS.UI..."
  dotnet build $proyecto 2>&1 | Select-Object -Last 3 | ForEach-Object { Log "  $_" }
  if ($LASTEXITCODE -ne 0) {
    Log "FALLO: la compilacion devolvio codigo $LASTEXITCODE."
    exit 1
  }
}

# --- 3) Entorno: MISMA configuracion que el arranque diario ---
# ASPNETCORE_ENVIRONMENT=Development es obligatorio con el simulador DGII activo.
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$pwdCert = [Environment]::GetEnvironmentVariable('Certificado__Password', 'User')
if ([string]::IsNullOrWhiteSpace($pwdCert)) {
  Log "FALLO: Certificado__Password no esta definida a nivel usuario (doc 21 §7.1)."
  exit 1
}
$env:Certificado__Password = $pwdCert

# --- 4) Arrancar el exe compilado (mismo patrón que usaba la instancia previa) ---
Log "Levantando POS.UI en $url ..."
Start-Process -FilePath $exe `
  -ArgumentList @('--urls', $url) `
  -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden `
  -RedirectStandardOutput (Join-Path $logDir 'pospalasy-app.log') `
  -RedirectStandardError  (Join-Path $logDir 'pospalasy-app.err.log')

# --- 5) Esperar /health (max ~90 s) ---
$salud = $null
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Seconds 3
  try {
    $salud = Invoke-RestMethod -Uri "$url/health" -TimeoutSec 3
    break
  } catch { }
}
if (-not $salud) {
  Log "FALLO: la app no respondio /health tras 90 s. Revisar $logDir\pospalasy-app.err.log"
  exit 1
}
Log "OK: app arriba en $url — /health = $salud"
if ($salud -ne 'Healthy') {
  Log "AVISO: /health reporta $salud (revisar mensajes en la app)."
}
Log "==== Reinicio completado ===="
exit 0
