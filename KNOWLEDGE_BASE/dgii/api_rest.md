# DGII — API REST de Facturación Electrónica

**Fuente:** Descripción Técnica Servicios DGII v1.7 (Mayo 2026) — dgii.gov.do
**Fecha:** 18/09/2026
**Extracción:** web_extract + análisis de XSDs

---

## 1. Arquitectura General

La DGII ofrece servicios web REST para facturación electrónica:

- **Lenguaje:** XML (formatos con XSD)
- **Transporte:** HTTPS / REST API
- **Autenticación:** OAuth 2.0 (Bearer Token) vía certificado digital
- **Formato respuesta:** JSON (principal) y XML
- **Ambientes:** Pre-certificación, Certificación, Producción

### Flujo típico de una factura electrónica

```
1. Autenticación
   GET  /api/autenticacion/semilla           → Semilla XML
   POST /api/autenticacion/validarsemilla   → Token (Bearer)

2. Composición e-CF
   Construir XML e-CF 32 → Calcular hash (6 dígitos) → Firmar XML-DSig

3. Envío
   POST /api/facturaselectronicas           (para e-CF ≥ RD$250,000)
   POST /api/recepcion/ecf                  (para RFCE < RD$250,000)
   → Recibir TrackId

4. Polling estado
   GET  /api/consultas/estado?trackid={id} → Aceptado/Rechazado/EnProceso

5. QR (opcional)
   Consultar timbre para representación impresa
```

---

## 2. Autenticación (OAuth 2.0)

### Obtener semilla

```
GET https://ecf.dgii.gov.do/{ambiente}/autenticacion/api/autenticacion/semilla
Accept: */*
```

**Respuesta XML:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<SemillaModel xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
              xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <valor>0000000-0000-0000-0000-000000000000</valor>
  <fecha>2019-03-13T14:33:32.8617792-04:00</fecha>
</SemillaModel>
```

### Validar semilla (obtener token)

Firmar el XML de semilla con el certificado digital y enviarlo:

```
POST https://ecf.dgii.gov.do/{ambiente}/autenticacion/api/autenticacion/validarsemilla
Content-Type: multipart/form-data
Authorization: (no requerido para esta petición)

FormData: xml=@semilla_firmada.xml
```

**Respuesta JSON:**
```json
{
  "token": "eyJhbGciOi...",
  "expira": "2026-09-18T20:30:00Z",
  "expedido": "2026-09-18T19:30:00Z"
}
```

**Tokens válidos por:** 1 hora

**Usar token en headers:**
```
Authorization: Bearer eyJhbGciOi...
```

---

## 3. URLs por Ambiente

### e-CF (envío de facturas completas, ≥ RD$250,000)

| Ambiente | URL Base |
|----------|----------|
| Pre-certificación | https://ecf.dgii.gov.do/testecf |
| Certificación | https://ecf.dgii.gov.do/certecf |
| Producción | https://ecf.dgii.gov.do/ecf |

### RFCE (resumen facturas de consumo, < RD$250,000)

| Ambiente | URL Base |
|----------|----------|
| Pre-certificación | https://fc.dgii.gov.do/testecf |
| Certificación | https://fc.dgii.gov.do/Certecf |
| Producción | https://fc.dgii.gov.do/ecf |

---

## 4. Servicios Web

### 4.1 Autenticación

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| GET | `/autenticacion/api/autenticacion/semilla` | Obtener archivo semilla |
| POST | `/autenticacion/api/autenticacion/validarsemilla` | Validar semilla firmada → token |

### 4.2 Recepción de e-CF (facturas completas)

**Usar cuando:** Facturas de consumo con monto ≥ RD$250,000.00, o cualquier otro tipo de comprobante.

```
POST https://ecf.dgii.gov.do/{ambiente}/recepcion/api/facturaselectronicas
Authorization: Bearer {token}
Content-Type: multipart/form-data

FormData: xml=@rNC+eNCF.xml
```

**Ejemplo nombre archivo:** `101672919E3100000001.xml`

**Respuesta:**
```json
{
  "trackId": "d2b6e27c-3908-46f3-afaa-2207b9501b4b",
  "error": "string (si hay error)",
  "mensaje": "string"
}
```

### 4.3 Recepción de RFCE (resumen factura de consumo)

**Usar cuando:** Facturas de consumo con monto < RD$250,000.00.

```
POST https://fc.dgii.gov.do/{ambiente}/recepcionfc/api/recepcion/ecf
Authorization: Bearer {token}
Content-Type: multipart/form-data

FormData: xml=@rNC+eNCF.xml
```

**Ejemplo nombre archivo:** `101672919E3200000001.xml`

**Respuesta JSON:**
```json
{
  "codigo": 1,
  "estado": "Aceptado",
  "mensajes": [{"codigo": "0", "valor": ""}],
  "encf": "E320000000001",
  "secuenciaUtilizada": true
}
```

**código estado:** 1=Aceptado, 2=Aceptado Condicional, 3=Rechazado

**secuenciaUtilizada:**
- `true` = NO puede reutilizarse la secuencia
- `false` = PUEDE reutilizarse (útil cuando fue rechazado por errores correctables)

### 4.4 Consulta de Resumen RFCE

```
GET https://fc.dgii.gov.do/{ambiente}/consultarfce/api/Consultas/Consulta?
    RNC_Emisor={rnc}&
    ENCF={encf}&
    Cod_Seguridad_eCF={hash6}

Authorization: Bearer {token}
Accept: application/json
```

**Respuesta:**
```json
{
  "rnc": "131880738",
  "encf": "E320000000001",
  "secuenciaUtilizada": true,
  "codigo": "1",
  "estado": "Aceptado",
  "mensajes": [{"valor": "", "codigo": 0}]
}
```

**Estados:** 0=No encontrado, 1=Aceptado, 2=Rechazado

### 4.5 Consulta de Resultado e-CF

```
GET https://ecf.dgii.gov.do/{ambiente}/consultaresultado/api/consultas/estado?trackid={trackid}

Authorization: Bearer {token}
Accept: application/json
```

**Respuesta:**
```json
{
  "trackId": "d2b6e27c-3908-46f3-afaa-2207b9501b4b",
  "codigo": 1,
  "estado": "Aceptado",
  "rnc": "130862346",
  "eNCF": "E310005000201",
  "secuenciaUtilizada": true,
  "fechaRecepcion": "2023-08-15T06:06:57",
  "mensajes": [{"valor": "", "codigo": 0}]
}
```

**Estados:** No encontrado, Aceptado, Rechazado, Aceptado Condicional, En proceso (promedio 200ms)

### 4.6 Consulta de Estado e-CF

```
GET https://ecf.dgii.gov.do/{ambiente}/consultaestado/api/consultaestado?
    rncemisor={rnc}&
    ncfelectronico={encf}&
    rnccomprador={rnc_comprador}&
    codigoseguridad={hash6}

Authorization: Bearer {token}
Accept: application/json
```

**Nota:** Requiere estar delegado como emisor o receptor.

**Respuesta:**
```json
{
  "codigo": 0,
  "estado": "string",
  "rncEmisor": "string",
  "ncfElectronico": "string",
  "montoTotal": 0,
  "totalTBIS": 0,
  "fechaEmision": "string",
  "fechaFirma": "string",
  "rncComprador": "string",
  "codigoSeguridad": "string",
  "idExtranjero": "string"
}
```

### 4.7 Consulta TrackIds

```
GET https://ecf.dgii.gov.do/{ambiente}/consultatrackids/api/trackids/consulta?
    rncemisor={rnc}&
    encf={encf}

Authorization: Bearer {token}
Accept: application/json
```

**Nota:** Requiere delegación del emisor. Retorna múltiples TrackIds si se enviaron varios e-CF con mismo e-NCF.

### 4.8 Recepción de Aprobación Comercial

```
POST https://ecf.dgii.gov.do/{ambiente}/aprobacioncomercial/api/aprobacioncomercial
Authorization: Bearer {token}
Content-Type: multipart/form-data

FormData: xml=@acecf_firmado.xml
```

**Respuesta:**
```json
{
  "mensaje": ["string"],
  "estado": "Aprobada",
  "codigo": "1"
}
```

### 4.9 Anulación de Rangos e-NCF

```
POST https://ecf.dgii.gov.do/{ambiente}/anulacionrangos/api/operaciones/anularrango
Authorization: Bearer {token}
Content-Type: multipart/form-data

FormData: xml=@anulacion_rangos.xml
```

**Validaciones:** Las secuencias no deben haber sido utilizadas. Para facturas de consumo, se valida que no hayan sido usadas con montos ≥ o < RD$250,000.

### 4.10 Consulta Directorio de Servicios

```
GET https://ecf.dgii.gov.do/{ambiente}/consultadirectorio/api/consultas/listado
Authorization: Bearer {token}
Accept: application/json
```

**Retorna:** Todos los contribuyentes electrónicos con sus URLs de recepción, aprobación y autenticación.

```
GET https://ecf.dgii.gov.do/{ambiente}/consultadirectorio/api/consultas/obtenerdirectorioporrnc?RNC={rnc}
Authorization: Bearer {token}
Accept: application/json
```

### 4.11 Consulta Timbre (QR)

```
GET https://ecf.dgii.gov.do/{ambiente}/consultatimbre?
    rncemisor={rnc}&
    rnccomprador={rnc_comprador}&
    encf={encf}&
    fechaemision={dd-mm-aaaa}&
    montototal={monto}&
    fechafirma={dd-mm-aaaa%20HH:MM:SS}&
    codigoseguridad={hash6}

Authorization: Bearer {token}
Accept: application/json
```

**Usar:** Para validar el QR de la representación impresa (RI). QR versión 8.

### 4.12 Consulta Timbre FC (RFCE QR)

```
GET https://fc.dgii.gov.do/{ambiente}/consultatimbrefc?
    rncemisor={rnc}&
    encf={encf}&
    montototal={monto}&
    codigosegurida={hash6}

Authorization: Bearer {token}
Accept: application/json
```

### 4.13 Comunicación Emisor-Receptor

Simula ser un contribuyente para interacción entre contribuyentes (modo prueba):

```
GET https://ecf.dgii.gov.do/testecf/emisorreceptor/fe/autenticacion/api/semilla
POST https://ecf.dgii.gov.do/testecf/emisorreceptor/fe/autenticacion/api/validacioncertificado
```

---

## 5. Endpoints Resumen

### e-CF (facturas completas, ≥250K)

| # | Método | Endpoint | Propósito |
|---|--------|----------|-----------|
| 1 | GET | `.../autenticacion/api/autenticacion/semilla` | Obtener semilla |
| 2 | POST | `.../autenticacion/api/autenticacion/validarsemilla` | Validar semilla + obtener token |
| 3 | POST | `.../recepcion/api/facturaselectronicas` | Enviar e-CF completo |
| 4 | GET | `.../consultaresultado/api/consultas/estado?trackid={id}` | Consultar resultado por TrackId |
| 5 | GET | `.../consultaestado/api/consultaestado?rncemisor=...&ncfelectronico=...` | Consultar estado de e-CF |
| 6 | GET | `.../consultatrackids/api/trackids/consulta?rncemisor=...&encf=...` | Listar TrackIds por e-NCF |
| 7 | POST | `.../aprobacioncomercial/api/aprobacioncomercial` | Enviar aprobación comercial |
| 8 | POST | `.../anulacionrangos/api/operaciones/anularrango` | Anular rangos de secuencias |
| 9 | GET | `.../consultadirectorio/api/consultas/listado` | Listar contribuyentes electrónicos |
| 10 | GET | `.../consultadirectorio/api/consultas/obtenerdirectorioporrnc?RNC=...` | Directorio por RNC |
| 11 | GET | `.../consultatimbre?rncemisor=...&encf=...&codigoseguridad=...` | Validar QR e-CF |
| 12 | GET | `.../emisorreceptor/fe/autenticacion/api/semilla` | Emisor-Receptor: obtener semilla |
| 13 | POST | `.../emisorreceptor/fe/autenticacion/api/validacioncertificado` | Emisor-Receptor: validar certificado |

### RFCE (resumen factura consumo, <250K)

| # | Método | Endpoint | Propósito |
|---|--------|----------|-----------|
| 1 | GET | `.../consultarfce/api/Consultas/Consulta?RNC_Emisor=...&ENCF=...&Cod_Seguridad_eCF=...` | Consultar estado RFCE |
| 2 | POST | `.../recepcionfc/api/recepcion/ecf` | Enviar RFCE |
| 3 | GET | `.../consultatimbrefc?rncemisor=...&encf=...&montototal=...&codigosegurida=...` | Validar QR RFCE |

---

## 6. Valores de Estado

| Código | Estado | Descripción |
|--------|--------|-------------|
| 0 | No encontrado | No se encontró el comprobante |
| 1 | Aceptado | Comprobante válido, tiene validez fiscal |
| 2 | Rechazado | Nulidad del comprobante |
| (extra) | Aceptado Condicional | No cumplió algo pero no amerita rechazo |
| (extra) | En proceso | Aún no validado (promedio 200ms) |

---

## 7. Código de Seguridad (Hash)

**Algoritmo según DGII:**
1. Tomar XML completo (incluyendo espacio para firma)
2. Canonicalizar
3. Calcular SHA256
4. Los **primeros 6 caracteres del hash (base64)** = CodigoSeguridadeCF
5. Este código va en el XML (antes de firmar) y en el QR

**Importante:** El código de seguridad se extrae de los primeros 6 dígitos del SignatureValue de la firma digital.

---

## 8. Nomenclatura de Archivos

```
Formato: RNC+eNCF.xml
Ejemplo: 101672919E3100000001.xml
```

---

## 9. Consideraciones Clave

1. **Dos servicios de recepción para tipo 32:** Si monto < RD$250,000 → RFCE. Si monto ≥ RD$250,000 → e-CF completo.
2. **SecuenciaUtilizada:** Si un RFCE fue rechazado por errores correctables (certificado inválido, XML mal formado, etc.), `secuenciaUtilizada=false` y se puede reutilizar la secuencia.
3. **QR versión 8:** Para representación impresa.
4. **Delegación:** Para consultar estado de e-CF de otro contribuyente, se debe estar delegado.
5. **Token expira en 1 hora:** Debe refrescarse.
6. **Contenido XML:** No incluir tags vacíos.
7. **Codificación:** UTF-8.

---

## 10. Referencias

- **Documento oficial:** Descripción Técnica Servicios DGII v1.7 (Mayo 2026)
- **Swagger/OpenAPI:** Disponible en cada URL base (`/help/index.html`)
- **RFC de tokens:** RFC 6750 (Bearer Token Usage)
