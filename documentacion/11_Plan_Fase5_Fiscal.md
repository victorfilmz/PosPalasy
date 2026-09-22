# 11 · Plan FASE 5 — Integración fiscal DGII (XML-DSig, XSD, autenticación, ANECF)

> **Autorización:** concedida por el propietario tras FASE 4 (`27c75e4`).
> **Regla de la fase:** cada sub-fase termina en commit propio con pruebas y gate; el gate final
> decide PASS / PASS WITH RISKS / FAIL antes de pasar a FASE 6. Nada fiscal se adelanta sin
> evidencia contra la especificación.

---

## 0. Investigación previa — estado real de la maquinaria fiscal

### 0.1 Lo que ya existe y funciona (no se rehace)
| Componente | Estado verificado |
|---|---|
| Máquina de estados del e-CF (`Creada→XmlGenerado→XsdValidado→Firmada→Encolada→Enviada→ConfirmadaEnvio` + `EnvioIncierto/ErrorTemporal/ErrorPermanente`) | implementada con transiciones guardadas |
| Outbox con lease, intentos máximos y worker en segundo plano | operativa desde FASE 2/3 |
| eNCF: secuencia serializada con `UPDLOCK` (FASE 3), índice único, 0 duplicados hasta 100 concurrentes | sólida |
| Validación XSD **ya cableada** en `ValidarXmlAsync` (ruta a `e-CF 32 v.1.0.xsd`) | presente, sin tests contra el XSD real |
| `XmlDigitalSigner` (RSA-SHA256, enveloped, C14N, X509 en KeyInfo) | implementado, **no integrado al pipeline** |
| Carga de certificado `.pfx/.p12` → `{datos}/emisor.pfx`, contraseña por `Certificado:Password` (no se persiste) | implementado en Configuración |
| Cliente REST JSON con simulador, clasificador de errores (recuperable/incierto/permanente) y envío firmado por lease | operativa |
| ANECF: entidad, tipo 41 en `TipoeCFType`, endpoint de anulación configurado | esqueleto, sin detalles de referencia |

### 0.2 Brechas reales detectadas (esta es la lista de trabajo)
| # | Brecha | Evidencia |
|---|---|---|
| B1 | **Código de seguridad incorrecto**: se calcula SHA-256 completo del XML (`XMLHash`); la API lo usa como `Cod_Seguridad_eCF` de 6 caracteres (parámetro de consulta `codigoseguridad={hash6}`) | `KNOWLEDGE_BASE/dgii/api_rest.md` §4.4; `GenerarHash` en el servicio |
| B2 | **Firma no integrada**: `EnviarAsync` transmite `XMLContent` sin `<ds:Signature>`; el estado `Firmada` nunca se alcanza | `EnviarAsync` líneas 273–274; doc 07 §3 |
| B3 | **Autenticación inexistente**: no hay semilla → firma → token; los envíos no llevan `Authorization: Bearer` | no existe ningún `Semilla/Token` en `src` |
| B4 | **Endpoints desalineados**: `DgiiConfig` apunta a `dfe.dgii.gov.do/fe/...`; la API documentada usa `ecf.dgii.gov.do/{ambiente}/recepcion/api/facturaselectronicas` (e-CF) y `fc.dgii.gov.do/{ambiente}/recepcionfc/api/recepcion/ecf` (RFCE) | `api_rest.md` §3/§4 |
| B5 | **Un solo camino de recepción**: la regla de negocio (factura de consumo < RD$ 250.000 → RFCE; ≥ 250.000 y demás tipos → e-CF completo) no está implementada | `api_rest.md` §4.2/§4.3 |
| B6 | **Resultado fiscal no modelado**: solo `trackId + Estado(string)`; faltan `codigo` (1=Aceptado, 2=Aceptado Condicional, 3=Rechazado), `mensajes[]` y `secuenciaUtilizada` | `api_rest.md` §4.3 |
| B7 | **XSD solo para tipo 32, ruta hardcodeada**: faltan 31/33/34 y mapa `TipoeCF → archivo XSD` | `ValidarXmlAsync` |
| B8 | **ANECF incompleto**: sin datos de referencia del comprobante anulado (eNCF, monto, fecha, RNC), sin serializer ni envío real | `AnularAsync` (parte del servicio), XSD `ANECF v.1.0.xsd` disponible |
| B9 | **Secuencia ante rechazo**: `secuenciaUtilizada=false` implica que la secuencia PUEDE reutilizarse tras un rechazo corregible; hoy la secuencia solo avanza | interacción con diseño FASE 3 |

### 0.3 WIP cuarentenado (`_wip_fase5/`) — veredicto de rescate
| Elemento | Decisión | Motivo |
|---|---|---|
| `IDigitalSignatureService` + `DigitalSignatureServiceAdapter` | **RESCATAR** (con ajustes) | puente limpio Application↔Infraestructura para inyectar la firma en el caso de uso |
| `XmlValidator.ValidateAsync` | **RESCATAR** (trivial) | firma asíncrona coherente con el pipeline |
| Reescritura de `DgiiElectronicInvoiceService` | **DESCARTAR** | eliminaba el corte `PrepararYRegistrar/Enviar` (rompe FASE 2/3); los defectos que señalaba se atienden en B1–B9 |
| `eNCF` de 12 caracteres "FA/NC/…", `ElectronicInvoiceContingency` con EF en Domain, `ApplicationDbContext`/`SqlInvoiceRepository` paralelos, proyecto `POS.Infrastructure.ElectronicInvoicing`, `ConfiguracionCambio` duplicado | **DESCARTAR** | fiscalmente incorrecto o arquitectónicamente incompatible; los duplicados de repositorios no sustituyen a los casos de uso |

### 0.4 Fuente de verdad de la especificación
- `KNOWLEDGE_BASE/dgii/api_rest.md` (440 líneas): endpoints, autenticación, RFCE/e-CF, consulta, anulación.
- `documentacion xsd/`: e-CF 31–47, ANECF, ACECF, ARECF, RFCE, Semilla (todos presentes).
- Cualquier divergencia entre doc 04 (diseño histórico, menciona SOAP) y `api_rest.md` (REST JSON) se
  resuelve **a favor de la KB + XSD reales**; doc 04 quedará anotado.

---

## 1. Objetivo y fuera de alcance

**IN (FASE 5):** pipeline fiscal completo ejecutable en **homologación (TestECF)**: XSD por tipo,
firma XML-DSig integrada, autenticación semilla→token, transmisión RFCE/e-CF según regla de 250k,
captura completa del resultado fiscal, ANECF funcional para nota de crédito desde devolución,
política de secuencia ante rechazo y corrección del código de seguridad.

**OUT (fases posteriores):** producción real (requiere certificación con la DGII), portal de
consultas, reportes 606/607/IT-1, régimen de contingencia completo (solo se documenta la máquina de
estados), RFCE como documento resumen independiente.

**Restricción heredada:** la venta sigue siendo la unidad transaccional (FASE 1–4). Ningún cambio de
esta fase toca la atomicidad de la venta ni la política `ERROR → ESTADO CONSISTENTE`.

---

## 2. Sub-fases, entregables y gates

### 5.0 — Contrato fiscal correcto: código de seguridad + mapa XSD (gate G0) ✅ EJECUTADA

> **Gate G0 — PASS (2026-09-19).** Evidencia: 239/239 pruebas (70 dominio + 70 ventas + 99 seguridad/E2E),
> build 0 errores/0 advertencias, arranque real contra SQL Server con 0 errores y XSDs oficiales copiados
> al output de la aplicación.
>
> **Hallazgos de la ejecución (el detector hizo su trabajo):**
> 1. El XML canónico **no** validaba contra el XSD oficial. Desviaciones corregidas: provincia en
>    formato de 2 dígitos (el XSD exige 6: `010000`), teléfono del emisor fuera de
>    `TablaTelefonoEmisor`, orden `IndicadorBienoServicio`/`DescripcionItem` invertido, y
>    `FechaHoraFirma` + hueco `ds:Signature` ausentes (obligatorios en la secuencia de la raíz).
> 2. El XSD exige **exactamente un elemento tras `FechaHoraFirma`**: la ranura de la firma
>    XML-DSig. El serializer la emite vacía desde la construcción: el documento nace estructural
>   mente completo y la firma (5.1) solo llena el hueco.
> 3. El tipo 31 exige **`FechaVencimientoSecuencia`** (emisión + 6 meses): añadida.
> 4. `MontoItem` del renglón pasa a ser el total de la línea **con** sus impuestos (única cifra
>    coherente con `MontoTotal`); la validación XSD ahora está activa en `PrepararYRegistrarAsync`
>    (`ERROR_XSD` revierte la venta; estado honesto `XsdValidado`).
>
> **Nota sobre B1:** el código de seguridad de 6 caracteres ya se emitía (la deuda del doc 07 §3
> estaba parcialmente resuelta); ahora se genera **criptográficamente por comprobante**
> (`GeneradorCodigoSeguridad`, sin caracteres ambiguos) en lugar de derivarse del XML, y la
> integridad del documento queda a cargo de la firma (5.1). `ValidarXmlAsync` acepta el tipo del
> comprobante; `HashGenerator` mantiene su contrato para la anulación.
1. **B1**: corregir `CodigoSeguridadeCF` a 6 caracteres alfanuméricos. Decisión técnica a tomar con
   evidencia del XSD/KB: si es derivable (función de RNC+eNCF) se calcula; si es aleatorio se genera
   una vez, se persiste junto al comprobante y viaja en consultas. `XMLHash` (SHA-256) queda como
   integridad interna del documento, separada del código fiscal.
2. **B7**: mapa `TipoeCF → XSD` (31–34 en alcance; 41+ con 5.4), copia de XSDs al output, resolución
   de ruta sin hardcode, y **primer test real de validación**: e-CF 32 válido pasa; XML corrupto
   falla con mensajes legibles.
3. **Gate G0:** validación XSD real verde (válido/inválido), código de seguridad documentado y
   persistido, build 0/0, suite intacta.

### 5.1 — Firma XML-DSig integrada (gate G1) ✅ EJECUTADA

> **Gate G1 — PASS (2026-09-19).** Evidencia: 13 pruebas nuevas (10 unitarias de firma/proveedor +
> 3 de integración sobre SQLite real), suite completa en verde, build 0 errores/0 advertencias.
>
> **Diseño implementado:**
> 1. `FirmadorComprobanteECF` REEMPLAZA el hueco estructural `ds:Signature` emitido por el serializer
>    (un apéndice dejaría dos firmas y el documento inválido) y **verifica la firma criptográficamente**
>    antes de entregarla (`SignedXml.CheckSignature` con el certificado incorporado en el propio
>    documento, la forma en que la DGII la comprobará).
> 2. `ProveedorCertificadoDigital`: resuelve `emisor.pfx` con la MISMA convención de rutas de
>    Configuración (absoluta se respeta; relativa ancla al directorio de datos), contraseña por
>    user-secrets/entorno (nunca versionada), clave privada efímera (`EphemeralKeySet`), caché
>    invalidada por fecha del archivo (instalar un certificado toma efecto sin reiniciar), aviso a 30
>    días de expirar.
> 3. Firma en `EnviarAsync` antes del base64, solo en modo REAL (el simulador no exige certificado;
>    el comportamiento de desarrollo queda intacto). Comprobantes ya firmados no se firman dos veces.
> 4. **Sin certificado utilizable no hay envío** (probado: 0 llamadas al cliente DGII): la ausencia
>    es un problema de configuración, no del documento → el comprobante vuelve a la cola con
>    reintento progresivo y sale solo al instalarse el certificado. Un documento cuya firma NO
>    verifica es `ErrorFirma` permanente. La contraseña errónea del `.pfx` (`CryptographicException`)
>    también se clasifica como recuperable de configuración, no zombi permanente.
> 5. El documento FIRMADO sigue validando contra el XSD oficial (probado contra `e-CF 32 v.1.0.xsd`)
>    y el XML firmado queda persistido para trazabilidad fiscal.

### 5.2 — Autenticación DGII: semilla → token (gate G2) ✅ EJECUTADA

> **Gate G2 — PASS (2026-09-19).** Evidencia: 5 pruebas nuevas con dobles de transporte (sin red),
> suite completa 256/256, build 0 errores/0 advertencias, arranque real 0 errores.
>
> **Diseño implementado:**
> 1. `DgiiAuthenticator`: GET semilla → firma del XML de semilla con el certificado del emisor
>    (mismo `FirmadorComprobanteECF`, firma verificable — probado) → POST `validarsemilla` (multipart,
>    campo `xml`) → token Bearer. Contrato exacto de `KNOWLEDGE_BASE/dgii/api_rest.md`.
> 2. **Caché thread-safe con doble verificación** bajo candado: vigencia 1 h, refresco temprano a los
>    55 min (ninguna operación fiscal arranca con un token a menos de 5 min de vencer).
> 3. `RenovarTokenAsync`: ruta de recuperación ante 401/403 — el cliente DGII adjunta el Bearer en
>    TODAS las llamadas (envío, consulta, aprobación comercial, anulación) y ante 401/403 renueva el
>    token UNA vez y reintenta; más de un ciclo lo clasifica el orquestador como error permanente.
> 4. El autenticador es **singleton en DI** (guarda la caché del token; un registro transitorio la
>    descartaría por scope y forzaría re-autenticación en cada envío). Sin certificado la
>    autenticación falla con `CERTIFICADO_AUSENTE` ANTES de pedir la semilla (0 llamadas).
> 5. El simulador no autentica (no hay red ni token en desarrollo) — el pipeline real queda listo
>    para homologación al desactivar `DGII:ModoSimulador`.

### 5.3 — Transmisión real RFCE/e-CF + resultado fiscal (gate G3) ✅ EJECUTADA

> **Gate G3 — PASS (2026-09-19).** Evidencia: 12 pruebas nuevas (transmisión sobre HTTP falso con
> captura de URL/contenido + integración del resultado fiscal con SQLite real), suite completa
> 268/268, build 0 errores/0 advertencias, arranque real 0 errores.
>
> **Diseño implementado:**
> 1. **B4 realineada:** `DgiiConfig` reescrita — ambiente (`TestECF/CertECF/Produccion`) deriva los
>    hosts oficiales (`ecf.`/`fc.dgii.gov.do`) y TODOS los endpoints (`DgiiEndpoints`); sin rutas
>    sueltas de configuración (fuente #1 de errores de integración). El token NO es intercambiable:
>    cada host autentica por separado (`DgiiAuthenticator` con doble caché).
> 2. **B5 realineada:** envío multipart/form-data con el nombre oficial `RNC+eNCF.xml` y endpoint
>    por regla de 250k: factura de consumo < 250k → `recepcionfc/api/recepcion/ecf` (RFCE); en caso
>    contrario (incluye ≥ 250k y tipos no consumo) → `recepcion/api/facturaselectronicas` (e-CF).
> 3. **B6 realineada:** respuesta fiscal completa (`codigo`, `estado`, `mensajes[]`,
>    `secuenciaUtilizada`, `trackId`). La recepción RFCE consolida el veredicto EN LA MISMA llamada
>    (sin TrackId): Aceptado/Aceptado Condicional → Aceptado con `FechaAprobacion`; Rechazado →
>    `MotivoRechazo` con el texto oficial; la recepción e-CF queda En proceso y se consolida por
>    la consulta de resultado (`?trackid=`). Toda la traza oficial persiste en el comprobante
>    (`EstadoDgii`, `MensajesDgii`, `SecuenciaUtilizada`).
> 4. Consulta RFCE por RNC/eNCF/código de seguridad integrada a `ConsultarEstadoAsync`; ANECF pasa
>    al endpoint oficial de anulación de rangos (multipart).
> 5. Configuración realineada (`"DGII:Ambiente": "TestECF"`), pantalla de Certificado muestra el
>    host e-CF real del ambiente; conectividad contra `ecf.dgii.gov.do/testecf`.

### 5.4 — Nota de crédito desde devolución (gate G4) ✅ EJECUTADA

> **Corrección fiscal del plan (documentada en el gate):** la evidencia del XSD `ANECF v.1.0.xsd` y
> de la KB demostró que el ANECF anula RANGOS de secuencias NO utilizadas y **no** es la nota de
> crédito de una devolución. La corrección de un comprobante se documenta con un **e-CF 34 (Nota de
> Crédito)** con `InformacionReferencia` al original; el ANECF queda para la anulación de rangos.
>
> **Gate G4 — PASS (2026-09-21).** Evidencia: 8 pruebas nuevas — 6 XSD reales del tipo 34 (nota
> canónica, `InformacionReferencia` en el orden del esquema, `IndicadorNotaCredito` obligatorio,
> rechazo sin referencia, rechazo por `CodigoModificacion` fuera de catálogo, rama del tipo 32
> intacta) + 2 de integración SQLite (devolución total → nota por el total con referencia completa
> y encolada; devolución parcial → nota por lo devuelto + reintento idempotente), suite 277/277,
> build 0 errores/0 advertencias, arranque real 0 errores.
>
> **Diseño implementado:**
> 1. Caso de uso `EmitirNotaCreditoDevolucionHandler` con autorización `Supervision`: UNA
>    transacción (ancla de serialización sobre la devolución + serie E34 + validación XSD oficial +
>    registro local + outbox), transmisión post-commit vía cola de emisión.
> 2. Idempotencia de dos niveles: verificación transaccional (una devolución, UNA nota) respaldada
>    por el índice único filtrado `IX_ElectronicInvoices_DevolucionId`, con su código de conflicto
>    mapeado (`IDEMPOTENCIA_NOTA_CREDITO`). El tope de reembolso y la idempotencia de devolución
>    (FASE 4) quedan intactos.
> 3. Fiscalidad: `IndicadorNotaCredito` 0/1 según los 30 días normativos (decisión del código, no de
>    UI), `CodigoModificacion=3` (corrige montos), renglones heredados de la devolución con el
>    tratamiento fiscal de cada línea vendida (la nota revierte exactamente el impuesto cobrado;
>    el ajuste de redondeo cae siempre en una línea gravada).

### 5.5 — Secuencia ante rechazo (gate G5)
1. **B9**: al recibir `secuenciaUtilizada=false` con rechazo corregible, registrar la secuencia como
   **reutilizable** (fila de hueco en `SecuenciasECF` o marca en el comprobante), con auditoría; la
   reasignación respeta el índice único y la serialización de FASE 3.
2. **Gate G5:** prueba: rechazo → siguiente venta reutiliza la secuencia liberada; 0 duplicados.

### 5.6 — Gate final de FASE 5
- Suite completa (≥ 232 previas + ~25 nuevas), build 0/0, arranque real, informe con el formato de
  gates anteriores y **veredicto**. Lista de verificación de homologación (credenciales TestECF,
  certificado de pruebas) como hand-off documentado.

---

## 3. Riesgos y mitigaciones
| Riesgo | Impacto | Mitigación |
|---|---|---|
| Sin credenciales/certificado de TestECF aún | toda la fase se valida con simulador | contratos exactos contra `api_rest.md` + dobles de transporte + hand-off para certificación |
| El serializer actual puede no validar contra el XSD real | retraso en 5.0 | 5.0 es precisamente el detector; se corrige campo por campo |
| Cambio de endpoints rompe el simulador y los 99 tests de seguridad | regresión | los cambios van detrás de `DgiiConfig`; simulador no depende de hosts |
| `secuenciaUtilizada` mal interpretada → duplicados | crítico | índice único + serialización FASE 3 como barrera; gate propio G5 |
| Otro agente/processo tocando el árbol (ocurrió en FASE 4) | corrupción | cuarentena `_wip_fase5/`, commits por sub-fase, verificación de `git status` antes de cada gate |

## 4. Orden, estimación y commits
```text
5.0 (2d) → 5.1 (3d) → 5.2 (2d) → 5.3 (4d) → 5.4 (4d) → 5.5 (2d) → gate final     ≈ 17 días
commit: feat(fase5.x): <alcance de la sub-fase>   por cada sub-fase
```

## 5. Criterios de gate (formato uniforme)
Cada gate reporta: evidencia (tests, build, arranque), desviaciones aceptadas, deuda nueva declarada
y veredicto. El gate final usa el formato del informe de FASE 3/4 (resumen, cambios, evidencia,
riesgos, veredicto) y **espera autorización para FASE 6**.
