# 21 — Plan de renovación del certificado digital

> **Propósito:** que el certificado que firma los e-CF **nunca venza en operación**. La DGII
> rechaza comprobantes firmados con certificado vencido **el mismo día del vencimiento y sin
> período de gracia** — ese día la facturación electrónica se detiene. Este plan alinea la
> alerta técnica de 30 días de PosPalasy con las tareas administrativas de renovación.
>
> **Fecha:** 23-09-2026 · Complementa docs 14 (trámite), 19 (paquete de solicitud),
> 20 (carta de autorización) y 16 §6 (operación diaria).

---

## 1. Capas de aviso del sistema

| Capa | Dónde | Cuándo dispara | Comportamiento |
|---|---|---|---|
| **Pantalla Configuración → Certificado** | UI de PosPalasy | Vigencia ≤ 30 días | Muestra estado "ATENCIÓN, expira en N días" (doc 14 §2) |
| **Health check `/health`** | Monitor externo | Vigencia ≤ 30 días | Reporta `Degraded` en el componente `certificado-digital` con fecha exacta de vencimiento |
| **Log** | `pospalasy-AAAAMMDD.log` | Cada verificación | Estado del certificado con fecha de vencimiento |

La alerta de **30 días** de PosPalasy es el umbral que gobierna este plan: en cuanto
`/health` reporte `Degraded` por vencimiento próximo, la fase D-30 del calendario (§3) está
activa — aunque nadie haya mirado el calendario.

> **El monitor 30/15/7/1 del §4 añade recordatorios ANTES de que la alerta de 30 días llegue al
> usuario** (D-60/D-45): el objetivo es que el trámite administrativo ya esté en marcha cuando
> el sistema empiece a degradar el health check.

---

## 2. Requisitos del certificado de reemplazo (no negociables)

La renovación repite el trámite del doc 19, con estos puntos críticos:

- **Perfil:** persona física, "Procesos/Tareas Tributarios" (Avansi/Viafirma) o "Certificado de
  Facturación Electrónica" (Digifirma). Nada de persona jurídica ni SSL.
- **Titular:** la **misma persona autorizada** de la carta del doc 20. Si cambió el firmante,
  tramitar primero la actualización ante DGII/certificadora (doc 20 §revocación) — un
  certificado de otra persona es rechazado en cada e-CF.
- **Corte de servicio:** entre la carga del nuevo `.pfx` y su verificación, los comprobantes
  quedan en cola con reintento (por diseño, no se pierden). Renovar **fuera de horario de
  ventas** (ver calendario §3).

---

## 3. Calendario alineado al monitor de 30 días

```text
        VENCIMIENTO DEL CERTIFICADO (Día 0)
Día     Acción                                          Gatillo / Evidencia
─────   ───────────────────────────────────────────    ─────────────────────────────
D-60    [A] Tarea programada dispara recordatorio       Evento PosPalasyCert Id 100
        [B] Arrancar renovación en la certificadora     Solicitud sometida
D-45    [C] Seguimiento: expediente aprobado            Evento Id 101 si no hay avance
        [D] Prueba de vida completada
D-30    ◄ POSPALASY DEGRADA /health (automático)        Health: "vence en N días"
        [E] Certificado nuevo emitido y descargado
        [F] Ventana de corte: cargar el .pfx nuevo
D-15    [G] Recordatorio programado (evento Id 102)     Evento + línea en log
        [H] Verificación en la app: puede firmar
D-7     [I] Última verificación cruzada (evento 103)    /health + Configuración
D-1     [J] Confirmar que el .pfx ACTIVO vence >30d     Health Healthy de nuevo
Día 0   Certificado viejo vence — sin impacto           La app ya firma con el nuevo
```

**Fase D-60 — inicio del trámite**

- [ ] Tarea programada "PosPalasy Recordatorio Certificado" dispara el recordatorio
      (evento Id 100 en el Visor de Eventos + línea en el log de PosPalasy)
- [ ] Iniciar la renovación en la certificadora (doc 19 §4: formulario, documentos, pago);
      es el mismo trámite de la primera vez, usualmente más rápido
- [ ] Confirmar que el firmante sigue siendo el autorizado (doc 20)

**Fase D-45 — seguimiento del expediente**

- [ ] Expediente aprobado por la certificadora; prueba de vida completada
- [ ] Si no hubo avance: re-clamar con la certificadora (la tarea del D-45 re-avisa)

**Fase D-30 — la alerta técnica de PosPalasy se activa sola**

- [ ] `/health` reporta `Degraded` en `certificado-digital` con la fecha exacta — ya no hay
      que confiar en memoria ni calendario: el sistema lo dice
- [ ] Certificado nuevo emitido: descargar el `.p12/.pfx` en UNA computadora
- [ ] Definir la contraseña nueva y registrarla en el gestor de secretos (no hay recuperación)
- [ ] **Ventana de corte** (fuera de horario): sustituir
      `%LOCALAPPDATA%\PosPalasy\certificados\emisor.pfx` por el nuevo — la app lo recarga
      sola al detectar el cambio de fecha de modificación (sin reinicio)
- [ ] Verificar en *Configuración → Certificado* la nueva fecha de vencimiento
- [ ] Respaldo del `.pfx` nuevo + contraseña en DOS lugares seguros

**Fase D-15 — verificación en profundidad**

- [ ] *Configuración → Certificado*: "El sistema puede firmar comprobantes" con el nuevo
- [ ] `/health` sigue mostrando la fecha nueva (Healthy si vence >30 días)
- [ ] Realizar una venta de prueba y confirmar que transmite/firma con el nuevo certificado

**Fase D-7 — última barrera**

- [ ] Confirmación cruzada final: `/health` Healthy + fecha nueva visible en Configuración
- [ ] Si algo falló: escalar — el certificado vence en 7 días y el viejo dejará de firmar

**Día 0 — el vencimiento llega sin impacto**: la app ya lleva semanas firmando con el nuevo.

---

## 4. Recordatorios automáticos (tarea programada + eventos de Windows)

Igual que el backup (doc 16 §4): automatización con dos canales de alerta — **log de
PosPalasy** y **Visor de Eventos** (origen `PosPalasyCert`), para que un monitoreo externo
pueda alertar sin leer archivos.

- `scripts/recordatorio_certificado.cmd` — lee la fecha de vencimiento real del `.pfx`
  (`Get-PfxData`), calcula días restantes y registra el evento Id 100/101/102/103 según el
  umbral (60/30/15/7). Idempotente y seguro si el `.pfx` falta.
- `scripts/instalar_recordatorio_certificado.cmd` — instalador (una vez, como administrador):
  registra la tarea programada **diaria a las 08:00** que ejecuta el recordatorio y corre una
  ejecución de prueba inmediata.

```text
Evento PosPalasyCert Id 100 (D-60) : "Renovación del certificado: inicie el trámite (N días)"
Evento PosPalasyCert Id 101 (D-30) : coincide con la degradación de /health
Evento PosPalasyCert Id 102 (D-15) : verificación en profundidad
Evento PosPalasyCert Id 103 (D-7)  : última barrera antes del vencimiento
```

Los eventos son el canal de integración: cualquier monitor (Zabbix, PRTG, un script de correo)
puede suscribirse al log de eventos y convertir el recordatorio en email/ticket.

---

## 5. Errores que este plan evita

1. **Dejarlo vencer**: la DGII rechaza e-CF el mismo día, sin gracia. El plan arranca el
   trámite a D-60, con dos recordatorios antes de que la alerta técnica siquiera se active.
2. **Renovar con otro firmante**: el certificado nuevo de otra persona se rechaza en cada
   comprobante. Verificación explícita en D-60.
3. **Perder la contraseña nueva**: sin ella el `.pfx` no sirve y no hay recuperación. Se
   registra en el gestor de secretos en el momento de definir (D-30).
4. **Renovar en horario pico**: el corte de carga del `.pfx` deja los comprobantes en cola;
   la ventana de corte es fuera de horario (D-30).
5. **Festejar antes de tiempo**: cargar el `.pfx` no basta — la verificación de D-15 exige una
   venta real de prueba firmada con el nuevo certificado.

---

## 6. Encadenamiento con el resto de la documentación

| Este plan | Documento |
|---|---|
| Trámite de renovación (mismos pasos) | Doc 14 §2, doc 19 |
| Identidad del titular y carta de autorización | Doc 20 |
| Alerta técnica de 30 días (UI + `/health`) | Doc 16 §3 y §6 |
| Custodia del `.pfx` y su contraseña | Doc 16 §5 (no se versionan) |
