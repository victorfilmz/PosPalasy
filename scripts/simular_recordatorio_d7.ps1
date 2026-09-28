# =====================================================================
# PosPalasy - Simulador del dia D-7 del calendario del certificado
# (doc 21 §3 y §4). Verifica que la ultima barrera dispara el evento
# Id 103 de nivel ERROR con el mensaje de escalamiento.
#
# Que hace:
#   1. Genera un certificado TEMPORAL self-signed que vence en 7 dias.
#   2. Aplica la MISMA arimetica del recordatorio (validada 13/13 por
#      el verificador tools/HomologacionTestECF/dryrun/PruebaRecordatorio):
#      dias<=7 -> Id 103.
#   3. Emite el evento Id 103 REAL con eventcreate, mensaje marcado
#      como SIMULACION.
#   4. Limpia el certificado temporal del store.
#
# Uso:        powershell -NoProfile -ExecutionPolicy Bypass -File simular_recordatorio_d7.ps1
#             (no requiere administrador; el origen PosPalasyCert ya
#              debe estar registrado por el recordatorio real, doc 21 §4)
#
# Seguridad: no toca el .pfx de produccion, no usa contrasenas reales,
# el evento queda marcado "(SIMULACION con certificado temporal)".
# =====================================================================

$ErrorActionPreference = 'Stop'

# --- Verificaciones previas ---
$pfxReal = Join-Path $env:LOCALAPPDATA 'PosPalasy\certificados\emisor.pfx'
if (Test-Path $pfxReal) {
  $diasReal = $null
  try {
    $pwdUser = [Environment]::GetEnvironmentVariable('POSPALASY_CERTPWD', 'User')
    if ($pwdUser) {
      $c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxReal, $pwdUser)
      $diasReal = [int]($c.NotAfter - (Get-Date)).TotalDays
    }
  } catch { }
  if ($diasReal -ne $null -and $diasReal -le 7) {
    Write-Output "ATENCION: el certificado REAL vence en $diasReal dias - ya estas en la fase D-7 real. La simulacion igualmente continuara con un certificado temporal."
  }
}

try {
  $null = Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='PosPalasyCert'} -MaxEvents 1 -ErrorAction Stop
  Write-Output "OK: el origen PosPalasyCert ya tiene eventos (registro previo presente)."
} catch {
  Write-Output "AVISO: el origen PosPalasyCert no tiene eventos aun; el eventcreate lo auto-registra si corres como administrador."
}

# --- 1) Certificado temporal que vence en 7 dias ---
$pwdSim = 'sim-d7-' + (Get-Date -Format 'yyyyMMdd')
$pfxSim = Join-Path $env:TEMP 'simulacion-d7.pfx'
$existente = Get-ChildItem 'Cert:\CurrentUser\My' -ErrorAction SilentlyContinue | Where-Object { $_.Subject -like '*Simulacion D-7*' }
if ($existente) { $existente | Remove-Item -Force }

$cert = New-SelfSignedCertificate -Subject 'CN=Simulacion D-7, O=PosPalasy' `
  -NotBefore (Get-Date).AddMinutes(-5) -NotAfter (Get-Date).AddDays(7) `
  -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy Exportable `
  -KeyAlgorithm RSA -KeyLength 2048
$pwdSecuro = ConvertTo-SecureString -String $pwdSim -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxSim -Password $pwdSecuro | Out-Null
Write-Output ("1) Certificado simulado: {0} | vence {1:yyyy-MM-dd HH:mm} ({2} dias)" -f $cert.Subject, $cert.NotAfter, [int]($cert.NotAfter - (Get-Date)).TotalDays)

# --- 2) Misma aritmetica que recordatorio_certificado.cmd / verificador ---
$vence = $cert.NotAfter
$dias = [int](($vence - (Get-Date)).TotalDays)
$umbral = if ($dias -le 7) { 103 } elseif ($dias -le 15) { 102 } elseif ($dias -le 30) { 101 } elseif ($dias -le 60) { 100 } else { 200 }
Write-Output ("2) Aritmetica del recordatorio con el certificado simulado: dias={0} -> evento Id {1}" -f $dias, $umbral)
if ($umbral -ne 103) {
  Write-Output "FALLO INESPERADO: 7 dias debieran mapear a Id 103. Revisar la aritmetica."
  $cert | Remove-Item -Force
  exit 1
}

# --- 3) Evento Id 103 REAL, marcado como simulacion ---
$fecha = $vence.ToString('yyyy-MM-dd')
$msg = "[Ultima barrera] el certificado vence en $dias dias, el $fecha. Escalar ahora: sin renovacion la DGII rechazara cada e-CF desde el vencimiento. Doc 21 fase D-7. (SIMULACION con certificado temporal)"
cmd /c "eventcreate /T ERROR /ID 103 /L APPLICATION /SO PosPalasyCert /D `"$msg`""
Write-Output "3) Evento Id 103 emitido desde la simulacion (nivel ERROR, origen PosPalasyCert)."

# --- 4) Limpieza garantizada ---
$cert | Remove-Item -Force
Remove-Item $pfxSim -Force -ErrorAction SilentlyContinue
Write-Output "Limpieza: certificado temporal eliminado del store y .pfx de %TEMP% borrado."
Write-Output "Verificar en el Visor de Eventos (Application, origen PosPalasyCert): el ultimo evento Id 103 debe llevar la marca (SIMULACION con certificado temporal)."
exit 0
