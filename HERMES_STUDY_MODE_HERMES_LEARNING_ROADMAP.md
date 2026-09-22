# HERMES LEARNING ROADMAP — ACTUALIZACIÓN POST INVESTIGACIÓN 1

**Fecha:** 18/09/2026
**Tarea completada:** Tarea 1 (Análisis DGII XSD + API REST)
**Estado:** Documentación generada en KNOWLEDGE_BASE/

---

## Hallazgos de la investigación

### DGII API REST (Descripción Técnica Servicios v1.7)

**13 servicios identificados:**

1. **Autenticación:** GET semilla → POST validar → token Bearer
2. **Recepción e-CF:** POST para montos ≥250,000
3. **Recepción RFCE:** POST para montos <250,000
4. **Consulta RFCE:** GET por RNC+ENCF+hash
5. **Consulta resultado:** GET por TrackId
6. **Consulta estado:** GET por RNC+ENCF+RNC comprador+hash
7. **Consulta TrackIds:** GET por RNC+ENCF
8. **Aprobación comercial:** POST ACECF firmado
9. **Anulación rangos:** POST rangos a anular
10. **Directorio servicios:** GET listado + GET por RNC
11. **Consulta timbre (QR):** GET para RI e-CF
12. **Consulta timbre FC (QR):** GET para RI RFCE
13. **Emisor-Receptor:** GET/POST para simulación

**Flujo completo de facturación:**

```
1. GET /api/autenticacion/semilla → XML semilla
2. Firmar semilla + POST /api/autenticacion/validarsemilla → Token (1 hora)
3. Construir XML e-CF 32
   - Calcular hash (SHA256 → 6 primeros chars base64) → CodigoSeguridadeCF
   - Insertar hash en XML
   - Firmar XML completo con certificado digital (XML-DSig)
4. POST /api/facturaselectronicas (si monto ≥250K) o
   POST /api/recepcion/ecf (si monto <250K)
   → Retorna TrackId (e-CF) o código+estado (RFCE)
5. Polling GET /api/consultaresultado → Aceptado/Rechazado/EnProceso
6. Generar QR para RI (si es RFCE o e-CF)
```

**Consideraciones clave descubiertas:**
- Dos endpoints de recepción para tipo 32 según monto (<>250,000 RD$)
- secuenciaUtilizada: si rechazado por errores correctables, puede reutilizarse
- XML-DSig requiere cálculo de hash ANTES de firmar
- Nombre archivo: RNC+eNCF.xml
- Swagger/OpenAPI disponible en cada URL base

---

### XSDs analizados (15 archivos)

| XSD | Tamaño | Elementos | Propósito |
|-----|--------|-----------|-----------|
| e-CF 32 | 123KB | 234 | Factura consumo (principal para POS) |
| e-CF 31 | 123KB | 237 | Factura crédito fiscal |
| RFCE 32 | 15KB | 41 | Contingencia (<250K) |

**Tipos simples críticos identificados:**
- TipoeCFType: 10 valores (31-47)
- IndicadorFacturacionType: 5 valores (0-4) → define ITBIS
- FormaPagoType: 8 valores
- TipoPagoType: 3 valores (contado/crédito/gratuito)
- RNCValidationType: 9 o 11 dígitos
- eNCFValidationType: 13 caracteres alfanuméricos
- Tipos impuestos adicionales: 28 código de impuestos
- UnidadMedidaType: 62 unidades de medida

---

## Knowledge Base creado

```
KNOWLEDGE_BASE/
├── code/
│   └── datacatalog.py          (Herramienta de análisis XSD)
└── dgii/
    ├── overview.md             (Visión general e-CF, estructura XML, ITBIS)
    ├── api_rest.md             (API REST completa con 13 servicios)
    ├── tipos_datos.md          (Tipos simples, enums, patrones XSD)
    ├── municipios.md           (Provincias y municipios RD)
    └── autoevaluacion.md       (Auto-evaluación: 2.0/5 promedio)
```

---

## Próximas tareas del roadmap

### Tarea 2: XML-DSig en .NET
- Estudiar System.Security.Cryptography.Xml
- Practicar: SignedXml, Canonicalization, X509Certificate2
- Código de ejemplo: firmar XML simple

### Tarea 3: Validación XSD en .NET
- XmlSchemaSet, XmlReaderSettings
- Validar XML e-CF 32 contra XSD
- Manejo de errores de validación

### Tarea 4: Servicio DGII de envío
- Integrar autenticación + envío e-CF
- Implementar polling de TrackId
- Timed retry para rechazos

### Tarea 5: Contingencia (RFCE 32)
- Persistir e-CF rechazados/pendientes
- Reprocesar cuando DGII disponible
- Diferencias XSD e-CF vs RFCE

---

## Estado de auto-evaluación

| Área | Antes | Después |
|------|-------|---------|
| XSDs DGII | 2 | 3 (mejor entendido) |
| ITBIS/Cálculos | 3 | 3 (confirmado) |
| XML-DSig | 1 | 1 (aún no estudiado) |
| API DGII | 2 | 4 (documentación oficial leída) |
| Contingencia | 2 | 2 |
| .NET + XML | 3 | 3 |
| Certificados | 1 | 1 |

**Promedio:** 2.0 → 2.6 (mejorado)

---

## Siguiente sesión

Quiero investigar:
1. XML-DSig: cómo firmar XML en .NET con certificado .p12
2. Validación XSD: XmlSchemaSet + XmlReader
3. Certificados digitales: gestión en Windows/.NET
