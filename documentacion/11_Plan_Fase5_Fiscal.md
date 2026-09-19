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

### 5.0 — Contrato fiscal correcto: código de seguridad + mapa XSD (gate G0)
1. **B1**: corregir `CodigoSeguridadeCF` a 6 caracteres alfanuméricos. Decisión técnica a tomar con
   evidencia del XSD/KB: si es derivable (función de RNC+eNCF) se calcula; si es aleatorio se genera
   una vez, se persiste junto al comprobante y viaja en consultas. `XMLHash` (SHA-256) queda como
   integridad interna del documento, separada del código fiscal.
2. **B7**: mapa `TipoeCF → XSD` (31–34 en alcance; 41+ con 5.4), copia de XSDs al output, resolución
   de ruta sin hardcode, y **primer test real de validación**: e-CF 32 válido pasa; XML corrupto
   falla con mensajes legibles.
3. **Gate G0:** validación XSD real verde (válido/inválido), código de seguridad documentado y
   persistido, build 0/0, suite intacta.

### 5.1 — Firma XML-DSig integrada (gate G1)
1. **B2**: firmar en `EnviarAsync` (antes del base64) vía `IDigitalSignatureService` (WIP rescatado);
   persistir XML firmado y alcanzar `Firmada` → `Encolada` de forma observable.
2. Certificado en runtime: resolver `emisor.pfx` + `Certificado:Password` con caché y **validación de
   vigencia** (avisar 30 días antes de expirar; error permanente claro si falta/está vencido).
3. Pruebas: la firma **verifica criptográficamente** (`SignedXml.CheckSignature` con el certificado
   público), el XSD sigue aceptando el documento firmado (enveloped permitido), sin cert no hay envío
   (error permanente, nada sale a la red).
4. **Gate G1:** firma verificable en pruebas, estados correctos, regresión 232+ verde.

### 5.2 — Autenticación DGII: semilla → token (gate G2)
1. **B3**: `DgiiAuthenticator`: GET semilla → firmar el XML de semilla (mismo signer) → POST
   `validarsemilla` → token Bearer **caché con expiración 1h (refresco a los 55 min, thread-safe)**;
   inyección del header en todos los envíos/consultas.
2. 401/403 → un solo refresco de token y reintento; si persiste, error permanente (credenciales).
3. Simulador coherente: token simulado sin red.
4. **Gate G2:** autenticación completa con dobles de transporte (semilla firmada verificable,
   renovación automática probada), simulador intacto.

### 5.3 — Transmisión real RFCE/e-CF + resultado fiscal (gate G3)
1. **B4/B5**: realinear `DgiiConfig` a la API documentada (hosts `ecf.`/`fc.` por ambiente
   testecf/certecf/ecf), **doble ruta por regla de 250k**, multipart con nombre `RNC+eNCF.xml`.
2. **B6**: modelo de respuesta completa (`codigo`, `mensajes[]`, `secuenciaUtilizada`, `trackId`);
   mapeo a estados: Aceptado→ConfirmadaEnvio; Aceptado Condicional→ConfirmadaEnvio con marca de
   observaciones; Rechazado→Rechazado **con mensajes persistidos** (consultables en Facturación).
3. Consulta de resultado (RFCE y e-CF) integrada a `ConsultarEstadoAsync` existente.
4. **Gate G3:** E2E simulador cubre los 3 resultados + envío incierto; outbox/lease/intentos
   intactos (regresión completa); UI muestra el resultado fiscal del comprobante.

### 5.4 — ANECF: nota de crédito desde devolución (gate G4)
1. **B8**: serializer ANECF completo según `ANECF v.1.0.xsd` (datos del comprobante referenciado,
   montos por forma de pago del desglose de la devolución — FASE 4 ya los calcula).
2. Caso de uso: `RegistrarDevolucion` marca la venta como "requiere NC"; generación/envío de ANECF
   como paso posterior (no dentro de la transacción de la devolución), con autorización
   `Supervision` y outbox propio.
3. **Gate G4:** ANECF valida contra su XSD; el flujo devolución→NC funciona en simulador; el tope de
   reembolso y la idempotencia de devolución quedan intactos.

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
