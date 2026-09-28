# =====================================================================
# PosPalasy - Arranque diario con verificacion previa (doc 21 §7).
# Valida en orden: variable de la contrasena -> certificado .pfx ->
# base de datos -> puerto libre. Solo si todo pasa levanta la app.
#
# Uso:        powershell -NoProfile -ExecutionPolicy Bypass -File iniciar_pospalasy.ps1
# Doble clic: iniciar_pospalasy.cmd
#
# La contrasena NUNCA se imprime; solo se informa existencia/longitud.
# =====================================================================

$ErrorActionPreference = 'Stop'

# --- Ubicaciones (el .ps1 vive junto a la solucion, en scripts/) ---
$repo    = Split-Path -Parent $PSScriptRoot
$appCsproj = Join-Path $repo 'src\POS.UI\POS.UI.csproj'
$logDir  = Join-Path $env:LOCALAPPDATA 'PosPalasy\logs'
$pfx     = Join-Path $env:LOCALAPPDATA 'PosPalasy\certificados\emisor.pfx'
$url     = 'http://localhost:5099'
$puerto  = 5099

if (-not (Test-Path $logDir))  { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
$log = Join-Path $logDir 'arranque-pospalasy.log'
function Log([string]$m) {
  $linea = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $m
  Write-Host $linea
  Add-Content -Path $log -Value $linea
}

Log "==== Arranque de PosPalasy ===="

# --- Si ya hay una instancia escuchando, no duplicar ---
$ocupado = Get-NetTCPConnection -LocalPort $puerto -State Listen -ErrorAction SilentlyContinue
if ($ocupado) {
  Log "AVISO: el puerto $puerto ya esta en uso (PID $($ocupado[0].OwningProcess)). La app parece estar corriendo; no se inicia otra."
  exit 0
}

# --- 1) Variable de la contrasena (alcance User, sin imprimirla) ---
$pwdCert = [Environment]::GetEnvironmentVariable('Certificado__Password', 'User')
if ([string]::IsNullOrWhiteSpace($pwdCert)) {
  Log "FALLO: Certificado__Password no esta definida a nivel usuario. Ver doc 21 §7.1."
  exit 1
}
Log "OK: Certificado__Password presente (longitud $($pwdCert.Length))."

# --- 2) Certificado: existe y abre con esa contrasena ---
if (-not (Test-Path $pfx)) {
  Log "FALLO: no existe el certificado en $pfx. Ver doc 16 §2."
  exit 1
}
try {
  $c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $pwdCert)
  $dias = [int]($c.NotAfter - (Get-Date)).TotalDays
  $vence = $c.NotAfter.ToString('dd-MM-yyyy')
  if ($dias -lt 0) {
    Log "FALLO: el certificado expiro el $vence. Renovacion urgente (doc 21 §3)."
    exit 1
  }
  if ($dias -le 30) {
    Log "AVISO: el certificado vence en $dias dias ($vence). /health reportara Degraded (doc 21 §4)."
  }
  Log "OK: certificado '$($c.Subject)' vence $vence ($dias dias)."
} catch {
  Log "FALLO: la contrasena no abre el .pfx (variable desactualizada o archivo corrupto). Actualizar Certificado__Password (doc 21 §7.1)."
  exit 1
}

# --- 3) Base de datos accesible (cadena de appsettings.json) ---
$appsettings = Get-Content (Join-Path $repo 'src\POS.UI\appsettings.json') -Raw | ConvertFrom-Json
$cs = $appsettings.ConnectionStrings.DefaultConnection
if ([string]::IsNullOrWhiteSpace($cs)) {
  Log "FALLO: ConnectionStrings:DefaultConnection no esta en appsettings.json."
  exit 1
}
$builderCs = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $cs
$server   = $builderCs['Server']
$database = $builderCs['Database']
try {
  $csMaster = "Server=$server;Database=master;Trusted_Connection=True;TrustServerCertificate=True"
  $cn = New-Object System.Data.SqlClient.SqlConnection $csMaster
  $cn.Open()
  $cmd = $cn.CreateCommand()
  $cmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = '$database'"
  $existe = [int]$cmd.ExecuteScalar()
  $cn.Close()
  if ($existe -eq 0) {
    Log "FALLO: la base '$database' no existe en $server. La app la crea al arrancar; revisar permisos."
    exit 1
  }
  Log "OK: base de datos '$database' accesible en $server."
} catch {
  Log "FALLO: no se pudo conectar a SQL Server ($server): $($_.Exception.Message)"
  exit 1
}

# --- 4) dotnet disponible ---
try { $null = dotnet --version } catch {
  Log "FALLO: dotnet CLI no disponible en PATH."
  exit 1
}

# --- Arranque: entorno limpio, la app lee Certificado__Password del usuario ---
Log "Levantando POS.UI en $url ..."
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Certificado__Password  = $pwdCert
$csprojQ = '"' + $appCsproj + '"'
Start-Process -FilePath 'dotnet' `
  -ArgumentList @('run', '--project', $csprojQ, '--no-launch-profile', '--urls', $url) `
  -WorkingDirectory $repo -WindowStyle Hidden `
  -RedirectStandardOutput (Join-Path $logDir 'pospalasy-app.log') `
  -RedirectStandardError  (Join-Path $logDir 'pospalasy-app.err.log')

# --- Espera a que /health responda (max ~60 s) ---
$salud = $null
for ($i = 0; $i -lt 24; $i++) {
  Start-Sleep -Seconds 3
  try {
    $salud = Invoke-RestMethod -Uri "$url/health" -TimeoutSec 3
    break
  } catch { }
}
if (-not $salud) {
  Log "FALLO: la app no respondio /health tras 72 s. Revisar $logDir\pospalasy-app.err.log"
  exit 1
}
Log "OK: app arriba en $url — /health = $salud"
if ($salud -ne 'Healthy') {
  Log "AVISO: /health reporta $salud (revisar mensajes en la app)."
}
Log "==== Arranque completado ===="
exit 0
