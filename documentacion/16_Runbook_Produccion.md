# 16 — Runbook de producción y estado de preparación

> **Fecha:** 23-09-2026 · **Alcance:** operación diaria, monitoreo y checklist de puesta en
> marcha. La homologación fiscal está en el doc 12/14; el plan de fase en el doc 15.

---

## 1. Estado de preparación (auditoría 23-09-2026)

| Área | Estado |
|---|---|
| Pipeline fiscal FASE 5 (XSD, firma, auth, transmisión, NC 34, ANECF, secuencias) | ✅ Completa, 310+ pruebas |
| Reportes fiscales 606/607/IT-1 con filtro de período | ✅ 607/IT-1 corregidos el 23-09 (bug de cumplimiento: antes procesaban todo el histórico) |
| Régimen de contingencia (6.1) | ✅ Declaración + ventana de 30 días; recuperación masiva (6.2) y portal (6.3) pendientes |
| Health checks (`/health`: BD, certificado, worker de cola) | ✅ Nuevo |
| Logging persistente a archivo con rotación diaria | ✅ Nuevo |
| Alerta de agotamiento de secuencias e-NCF (≥90% consumido) | ✅ Nuevo |
| Homologación real testecf | 🔴 Pendiente de credenciales (doc 14) |
| Backup de SQL Server | ✅ **Instalado y verificado 23-09-2026**: tarea programada "PosPalasy Backup Diario" (23:00, retención 30 días), primer .bak generado y validado |
| Repositorio remoto | 🔴 Push pendiente: los commits locales deben respaldarse (ver §5) |

## 2. Checklist de puesta en marcha

1. **Base de datos:** SQL Server/LocalDB con `ConnectionStrings:DefaultConnection`; el esquema y
   la semilla se crean solos en el arranque (`DbInitializer`, DDL idempotente).
2. **Certificado A1:** `.pfx` en `%LOCALAPPDATA%\PosPalasy\certificados\emisor.pfx`;
   contraseña por **user-secrets o variable de entorno** (`Certificado:Password`) — nunca en
   `appsettings.json`. Verificar en *Configuración → Certificado*: "El sistema puede firmar".
3. **Configuración DGII:**
   - `DGII:ModoSimulador = false` (la guarda de arranque FALLA si está en true fuera de Development — es deliberado)
   - `DGII:Ambiente = TestECF` durante homologación; `Produccion` tras certificar
   - `DGII:GrabarTransmisiones` solo tiene efecto en Development (cofre de contratos)
4. **Secuencias e-NCF:** cargar en *Configuración* los rangos E31/E32/E34 autorizados por la DGII.
5. **Administrador inicial:** contraseña por user-secrets; si queda vacía se genera temporal en el
   log y se obliga a cambiarla en el primer acceso.

## 3. Monitoreo

- **`GET /health`** → 200 si BD + certificado + worker OK; **503** si la BD falla; el cuerpo
  reporta qué componente está `Degraded` (certificado ausente/vencido, worker parado, certificado
  por vencer ≤30 días). Sin sesión: pensado para un monitor externo; no expone datos internos.
- **Logs:** `%LOCALAPPDATA%\PosPalasy\logs\pospalasy-AAAAMMDD.log`, rotación diaria, nivel
  Information+ (EF en Warning para no llenar disco). Buscar en incidentes:
  - `SECUENCIAS POR AGOTAR` — avisar a operación: pedir rango nuevo a la DGII
  - `SECUENCIA_AGOTADA` — las ventas ya están fallando: acción inmediata
  - `CONTINGENCIA` / `EnvioIncierto` — problemas de transmisión; la cola reintentará sola
  - `Error no controlado procesando la cola` — revisar trace completo

## 4. Backups — INSTALADO (23-09-2026)

La base contiene ventas, numeración fiscal y trazabilidad DGII: **backup diario obligatorio**.

**Instalado y verificado:** tarea programada de Windows **"PosPalasy Backup Diario"** (diaria a
las 23:00, `LastTaskResult: 0` en ejecución de prueba forzada). Componentes:

- `scripts/backup_pospalasy.sql` — BACKUP con INIT/CHECKSUM + RESTORE VERIFYONLY. **Sin
  COMPRESSION**: LocalDB/Express no la soporta (hallazgo real de la instalación).
- `scripts/ejecutar_backup_pospalasy.cmd` — wrapper de la tarea: sqlcmd por **ruta absoluta**
  (el perfil SYSTEM puede no tenerlo en PATH) + retención `forfiles` 30 días. La lógica vive en
  el wrapper porque `/TR` de schtasks está limitado a 261 caracteres.
- `scripts/instalar_backup_pospalasy.cmd` — instalador (ejecutar UNA vez como administrador:
  crea `C:\Backups\PosPalasy`, registra la tarea y corre un backup de prueba).
- Destino: `C:\Backups\PosPalasy\PosPalasy_DGII_<AAAAMMDD>.bak` (~7.4 MB, 914 páginas, validado).

**Verificación mensual obligatoria:** restaurar el último `.bak` en una base de prueba y comprobar
que `ElectronicInvoices` y `Ventas` tienen datos. En un servidor SQL de producción (no LocalDB)
se puede re-habilitar COMPRESSION. La aplicación no versiona el `.pfx` ni las contraseñas:
documentar su custodia aparte.

## 5. Repositorio

Los commits de FASE 5/6 existen solo localmente (remoto: `github.com/victorfilmz/PosPalasy`).
Hacer push tras validar este bloque. El `.pfx`, contraseñas y datos de clientes NO se versionan.

## 6. Operación diaria

| Cuándo | Qué |
|---|---|
| Apertura | Verificar `/health` = 200; revisar log del día anterior sin `SECUENCIA_AGOTADA` |
| En operación | Comprobantes con falla de transmisión quedan en cola: la pantalla *Facturación* muestra su estado; no reenviar a mano salvo desde el portal (6.3 pendiente) |
| Contingencia | Si la DGII cae, la venta sigue y el comprobante se marca con ventana de 30 días; al volver, la cola transmite sola |
| Cierre de mes | Generar 606/607/IT-1 del período (con filtro correcto), revisar el TXT y remitir |
| Certificado | La UI y `/health` avisan 30 días antes de vencer — renovar con tiempo |
