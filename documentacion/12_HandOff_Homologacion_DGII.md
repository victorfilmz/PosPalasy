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
| Transmisión real RFCE/e-CF según regla de 250k | ✅ Implementada (5.3) | Multipart con nombre oficial `RNC+eNCF.xml`; hosts/rutas oficiales derivados del ambiente (`ecf.`/`fc.dgii.gov.do`). |
| Resultado fiscal completo (`codigo`, `mensajes[]`, `secuenciaUtilizada`) | ✅ Implementado (5.3) | RFCE consolida el veredicto en la misma recepción; e-CF por consulta de resultado (`?trackid=`); traza persistida en el comprobante. |
| Nota de crédito de devolución (e-CF 34) | ✅ Demostrado en pruebas (XSD oficial + integración) | Gate G4 — sub-fase **5.4** |
| Reutilización de secuencia tras rechazo | ✅ Demostrado en pruebas (pool + auditoría + índice filtrado) | Gate G5 — sub-fase **5.5** |

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
| `DGII:Ambiente` | `TestECF` | Deriva los hosts y rutas oficiales por ambiente (`TestECF`/`CertECF`/`Produccion`); NO se configuran hosts a mano. |
| `Certificado:RutaCertificado` | `emisor.pfx` (o ruta absoluta) | Resuelto contra el directorio de datos si es relativo. |
| `Certificado:DirectorioDatos` | (vacío = `%LOCALAPPDATA%\PosPalasy\certificados`) | Fuera del directorio de la app y del contenido web. |
| `Certificado:Password` | **user-secrets / variable de entorno** | NUNCA en `appsettings.json` ni versionado. |

**Verificación previa obligatoria:** la pantalla *Configuración → Certificado* debe mostrar
**"El sistema puede firmar comprobantes"** antes de intentar cualquier envío. Si muestra que NO
puede firmar, los comprobantes quedarán en cola con reintento (por diseño, no se pierden).

## 4. Plan de verificación en `testecf` (casos en orden) — CHECKLIST EJECUTABLE

> **Ejecutable automatizado:** los 8 casos de esta sección corren con un solo comando
>
> ```bash
> dotnet run --project tools/HomologacionTestECF
> ```
>
> El verificador (`tools/HomologacionTestECF/README.md`) reutiliza los componentes reales del
> pipeline (serializer XSD, firmador XML-DSig, autenticador, cliente DGII), valida pre-requisitos,
> ejecuta los casos en orden con aserciones, y produce informe + bitácora de contratos en
> `artifacts/homologacion-testecf/`. Ensayado de punta a punta contra una DGII falsa con
> verificación criptográfica de firma (dry-run en `tools/HomologacionTestECF/dryrun/`):
> 8/8 PASS. En testecf real se ejecuta con las credenciales del checklist §2.
>
> **Preparación (una sola vez, antes del caso 1):**
>
> | # | Paso | Verificación |
> |---|---|---|
> | P1 | Instalar el `.pfx` del emisor | Configuración → Certificado: "El sistema puede firmar comprobantes" |
> | P2 | Cargar rangos e-NCF E31/E32 de testecf | Configuración → Secuencias: rango activo visible |
> | P3 | `user-secrets` con `Certificado:Password` | `dotnet user-secrets set "Certificado:Password" "..."` |
> | P4 | Arrancar en Development con cofre activo | `appsettings.Development.json`: `DGII:GrabarTransmisiones=true`, `ModoSimulador=false` |
> | P5 | Abrir el cofre | `artifacts/cofre-dgii/sesion-*.json` debe crearse con la primera respuesta |
>
> **Después de cada caso:** guardar la respuesta del cofre y compararla con los fixtures de
> `GrabadorTransmisionesDGIITests` — cualquier diferencia de contrato se ajusta en el parser del
> método correspondiente y se convierte en un nuevo fixture.


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
| **Código (FASE 5)** | Completa: 5.0–5.5 ejecutadas (gates G0–G5 PASS) y gate final 5.6 PASS (293/293, build 0/0, arranque real 0 errores — doc 13); pendiente solo el ejercicio real con credenciales TestECF |
| **Conjunto** | Ejecutar el plan de verificación §4 en testecf; registrar respuestas reales y ajustar contratos si difieren de la KB |
