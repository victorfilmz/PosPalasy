# 12 — Hand-off de Homologación DGII (TestECF)

> **Propósito:** entregar todo lo necesario para pasar del estado actual (pipeline fiscal completo
> demostrado en local/simulador) a la **pre-certificación en `testecf`** con la DGII real, dejando
> explícito qué está listo, qué falta en código y qué depende de la operación (credenciales,
> certificado, habilitación del RNC).
>
> **Fecha:** 19-09-2026 · Último commit fiscal: `241004d` (5.2)

---

## 1. Estado del sistema (evidencia, no promesas)

| Capacidad | Estado | Demostración |
|---|---|---|
| XML e-CF válido contra XSD oficial por tipo (31–47) | ✅ | Test detector contra `e-CF 32 v.1.0.xsd`; validación activa en la venta (`ERROR_XSD` revierte) — 5.0 |
| Código de seguridad de 6 caracteres por comprobante | ✅ | `GeneradorCodigoSeguridad` (criptográfico, sin caracteres ambiguos) |
| Firma XML-DSig RSA-SHA256 con verificación previa | ✅ | La firma verifica con el certificado incorporado en el documento; el firmado sigue validando XSD — 5.1 |
| Certificado en runtime (carga, caché, vigencia, diagnóstico) | ✅ | `ProveedorCertificadoDigital` + pantalla de Configuración ("¿puedo firmar?") |
| Autenticación semilla → firmar → token Bearer (caché 1h, refresco 55 min) | ✅ | Doble de transporte: la semilla firmada verifica criptográficamente; 401 → renovación + un reintento — 5.2 |
| Transmisión real RFCE/e-CF según regla de 250k | ⛔ Pendiente | Sub-fase **5.3** (el cliente transmite hoy por endpoint heredado JSON) |
| Resultado fiscal completo (`codigo`, `mensajes[]`, `secuenciaUtilizada`) | ⛔ Pendiente | Sub-fase **5.3** |
| ANECF (nota de crédito desde devolución) | ⛔ Pendiente | Sub-fase **5.4** |
| Reutilización de secuencia tras rechazo | ⛔ Pendiente | Sub-fase **5.5** |

Suite actual: **258/258 pruebas** · build 0/0 · arranque real contra SQL Server verificado.

---

## 2. Checklist de credenciales y certificado (responsabilidad de la operación)

- [ ] **Certificado digital .pfx/.p12** (tipo A1) emitido por entidad autorizada en RD
      (Avansi, Cámara de Comercio, etc.), a nombre del contribuyente emisor (el CN/RNC del
      certificado debe corresponder al RNC emisor de los comprobantes).
      - Con **clave privada** y contraseña conocida.
      - **Vigente** (la pantalla de Configuración avisa 30 días antes de expirar).
- [ ] **RNC habilitado como emisor electrónico** ante la DGII (inscripción al régimen e-CF) y
      acceso al ambiente de **pre-certificación `testecf`**.
- [ ] **eNCF disponibles** para el RNC emisor: las secuencias E31/E32 que la DGII tiene asociadas
      al contribuyente (el sistema las consume de `SecuenciasECF`; verificar rango activo en el
      portal DGII).
- [ ] **Credenciales de acceso al portal** para consultar resultados de la homologación.

## 3. Configuración del sistema (responsabilidad técnica)

| Clave | Valor para homologación | Nota |
|---|---|---|
| `DGII:ModoSimulador` | **`false`** | Activa firma + autenticación reales. La guarda del sistema impide simulador fuera de Development. |
| `DGII:BaseUrl` | `https://ecf.dgii.gov.do/testecf` | Punto único de hosts (5.3 realineará ecf./fc. + ambientes). |
| `Certificado:RutaCertificado` | `emisor.pfx` (o ruta absoluta) | Resuelto contra el directorio de datos si es relativo. |
| `Certificado:DirectorioDatos` | (vacío = `%LOCALAPPDATA%\PosPalasy\certificados`) | Fuera del directorio de la app y del contenido web. |
| `Certificado:Password` | **user-secrets / variable de entorno** | NUNCA en `appsettings.json` ni versionado. |

**Verificación previa obligatoria:** la pantalla *Configuración → Certificado* debe mostrar
**"El sistema puede firmar comprobantes"** antes de intentar cualquier envío. Si muestra que NO
puede firmar, los comprobantes quedarán en cola con reintento (por diseño, no se pierden).

## 4. Plan de verificación en `testecf` (casos en orden)

1. **Autenticación real:** arranque con `ModoSimulador=false` → intentar autenticación.
   *Éxito:* token emitido (log "Token DGII renovado"). *Fallo típico:* semilla rechazada
   (certificado no corresponde al RNC habilitado).
2. **e-CF 32 (consumo) firmado de extremo a extremo:** venta POS → envío → `TrackId` →
   consulta → **Aceptado**. Es el caso de paso obligatorio.
3. **e-CF 31 (crédito fiscal):** valida `FechaVencimientoSecuencia` (emisión + 6 meses).
4. **Resultado:** consultar en el portal DGII que el comprobante aparece con estado **Aceptado**
   y que `secuenciaUtilizada=true`.
5. **Firma alterada (negativo):** enviar un documento cuya firma no verifique → la DGII debe
   rechazarlo; verificar que el sistema lo clasifica y persiste el motivo.
6. **Envío duplicado:** reintentar un comprobante ya confirmado → el sistema NO lo reenvía
   (idempotencia local probada).
7. **Envío incierto:** simular corte de red durante el envío → el sistema marca `EnvioIncierto`
   y se resuelve **consultando** el TrackId, nunca reenviando a ciegas (probado en local).
8. **Corte prolongado:** verificar que la cola reintenta con espera progresiva y que al volver la
   conectividad los comprobantes salen solos.

## 5. Criterio de éxito de la homologación

```text
Al menos un e-CF 32 y un e-CF 31 firmados por POSPalasy aparecen como ACEPTADO
en el ambiente testecf (TrackId + portal DGII), con reintentos y cortes
ejercitados sin pérdida ni duplicación de comprobantes.
```

## 6. Riesgos y limitaciones conocidas

| Riesgo | Mitigación |
|---|---|
| Contrato JSON asumido de la KB (`token`, `trackId`, claves de resultado) | Punto único de ajuste por método del cliente; registrar la respuesta real en el primer contacto |
| XSD locales v1.0 | El mapa de XSD es el punto único de actualización si la DGII publica revisiones |
| Variantes de firma (política XAdES) no descartadas | El firmador es un componente único; se ajusta en homologación si la DGII lo exige |
| `ModoSimulador=false` accidentalmente en producción sin certificado | No se transmite nada (por diseño); los comprobantes esperan en cola con reintento |

## 7. Reparto de responsabilidades

| Quién | Qué |
|---|---|
| **Operación** | Certificado vigente + habilitación del RNC + acceso a testecf + eNCF activas |
| **Código (FASE 5)** | Sub-fases 5.3 (endpoints reales + regla 250k + resultado fiscal), 5.4 (ANECF), 5.5 (secuencia ante rechazo) antes del primer envío real |
| **Conjunto** | Ejecutar el plan de verificación §4 en testecf; registrar respuestas reales y ajustar contratos si difieren de la KB |
