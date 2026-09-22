# KNOWLEDGE_BASE/dgii/overview.md

**Fuente:** DGII oficial (dgii.gov.do) + análisis de XSDs + GitHub vítors1681/dgii-ecf
**Fecha:** 18/09/2026
**Estado:** Investigación inicial completada

---

## 1. ¿Qué es e-CF?

**e-CF (Comprobantes Fiscales Electrónicos)** es el sistema de facturación electrónica de la DGII (Dirección General de Impuestos Internos) de la República Dominicana.

- **Implementación gradual:** desde mayo 2024 hasta 2027 (cobertura universal)
- **Obligatorio** para grandes contribuyentes y medianos (lista publicada por DGII)
- **Esquema:** XML firmado digitalmente + envío a DGII vía API

---

## 2. Tipos de Comprobante (TipoeCFType)

| Valor | Nombre | Uso |
|-------|--------|-----|
| 31 | Factura de Crédito Fiscal Electrónica | Ventas a empresas con derecho de crédito ITBIS |
| 32 | Factura de Consumo Electrónica | Venta al cliente final (consumo) — **el más usado en POS** |
| 33 | Nota de Débito Electrónica | Corrección por aumento de monto |
| 34 | Nota de Crédito Electrónica | Corrección por disminución de monto |
| 41 | Compras Electrónico | Compras (invertido) |
| 43 | Gastos Menores Electrónico | Gastos menores |
| 44 | Regímenes Especiales Electrónico | Regímenes especiales |
| 45 | Gubernamental Electrónico | Entidades gubernamentales |
| 46 | Exportaciones Electrónico | Exportaciones |
| 47 | Pagos al Exterior Electrónico | Pagos al exterior |

> **Para POS:** TipoeCF = 32 (Factura de Consumo) es el caso principal.

---

## 3. Estructura XML e-CF 32

El XML tiene dos secciones principales:

### Sección A: Encabezado (ECF/Encabezado)

```
ECF
├── Encabezado
│   ├── Version (1.0)
│   ├── IdDoc
│   │   ├── TipoeCF (32)
│   │   ├── eNCF (13 chars alfanuméricos)
│   │   ├── IndicadorEnvioDiferido (opcional, 1)
│   │   ├── IndicadorMontoGravado (0=ITBIS no incluido, 1=ITBIS incluido)
│   │   ├── IndicadorServicioTodoIncluido (opcional, 1)
│   │   ├── TipoIngresos (01-06)
│   │   ├── TipoPago (1=Contado, 2=Crédito, 3=Gratuito)
│   │   ├── FechaLimitePago (opcional)
│   │   ├── TerminoPago (opcional, max 15)
│   │   ├── TablaFormasPago (opcional, hasta 7)
│   │   ├── TipoCuentaPago (CT/AH/OT)
│   │   ├── NumeroCuentaPago (max 28)
│   │   ├── BancoPago (max 75)
│   │   ├── FechaDesde (opcional)
│   │   ├── FechaHasta (opcional)
│   │   └── TotalPaginas (opcional, >1)
│   ├── Emisor (obligatorio)
│   │   ├── RNCEmisor (9 o 11 dígitos)
│   │   ├── RazonSocialEmisor (max 150)
│   │   ├── NombreComercial (opcional, max 150)
│   │   ├── Sucursal (opcional, max 20)
│   │   ├── DireccionEmisor (max 100)
│   │   ├── Municipio (código provincia/municipio)
│   │   ├── Provincia (código)
│   │   ├── TablaTelefonoEmisor (opcional, hasta 3: XXX-XXX-XXXX)
│   │   ├── CorreoEmisor (max 80, email)
│   │   ├── WebSite (max 50)
│   │   ├── ActividadEconomica (max 100)
│   │   ├── CodigoVendedor (max 60)
│   │   ├── NumeroFacturaInterna (max 20)
│   │   ├── NumeroPedidoInterno (max 20 dígitos)
│   │   ├── ZonaVenta (max 20)
│   │   ├── RutaVenta (max 20)
│   │   ├── InformacionAdicionalEmisor (max 250)
│   │   └── FechaEmision (DD-MM-AAAA)
│   ├── Comprador (obligatorio en e-CF 32)
│   │   ├── RNCComprador (9/11 dígitos, opcional si persona natural)
│   │   ├── IdentificadorExtranjero (max 20, si extranjero sin RNC)
│   │   ├── RazonSocialComprador (max 150)
│   │   ├── ContactoComprador (max 80)
│   │   ├── CorreoComprador (email)
│   │   ├── DireccionComprador (max 100)
│   │   ├── MunicipioComprador (código)
│   │   ├── ProvinciaComprador (código)
│   │   ├── FechaEntrega (opcional)
│   │   ├── ContactoEntrega (max 100)
│   │   ├── DireccionEntrega (max 100)
│   │   ├── TelefonoAdicional
│   │   ├── FechaOrdenCompra (opcional)
│   │   ├── NumeroOrdenCompra (max 20)
│   │   ├── CodigoInternoComprador (max 20)
│   │   ├── ResponsablePago (alfa, max 20)
│   │   └── InformacionAdicionalComprador (max 150)
│   ├── InformacionesAdicionales (opcional)
│   ├── Transporte (opcional)
│   ├── Totales (obligatorio)
│   │   ├── MontoGravadoTotal
│   │   ├── MontoGravadoI1 (18%)
│   │   ├── MontoGravadoI2 (16%)
│   │   ├── MontoGravadoI3 (0% ITBIS)
│   │   ├── MontoExento
│   │   ├── ITBIS1 (18)
│   │   ├── ITBIS2 (16)
│   │   ├── ITBIS3 (0)
│   │   ├── TotalITBIS
│   │   ├── TotalITBIS1
│   │   ├── TotalITBIS2
│   │   ├── TotalITBIS3
│   │   ├── MontoImpuestoAdicional
│   │   ├── ImpuestosAdicionales (hasta 20)
│   │   ├── MontoTotal (OBLIGATORIO)
│   │   ├── MontoNoFacturable
│   │   ├── MontoPeriodo
│   │   ├── SaldoAnterior
│   │   ├── MontoAvancePago
│   │   └── ValorPagar
│   └── OtraMoneda (opcional, para facturas en moneda extranjera)
├── DetallesItems (obligatorio)
│   └── Item (hasta 1000, cada uno con:)
│       ├── NumeroLinea (1-1000)
│       ├── TablaCodigosItem (opcional, hasta 5 códigos)
│       ├── IndicadorFacturacion (0-4)
│       ├── NombreItem (max 80)
│       ├── IndicadorBienoServicio (1=Biens, 2=Servicio)
│       ├── DescripcionItem (max 1000)
│       ├── CantidadItem
│       ├── UnidadMedida (código 1-62)
│       ├── CantidadReferencia
│       ├── UnidadReferencia
│       ├── TablaSubcantidad (opcional, hasta 5)
│       ├── GradosAlcohol (para bebidas)
│       ├── PrecioUnitarioReferencia (opcional)
│       ├── FechaElaboracion (opcional)
│       ├── FechaVencimientoItem (opcional)
│       ├── Mineria (opcional, para minería)
│       ├── PrecioUnitarioItem (obligatorio)
│       ├── DescuentoMonto (opcional)
│       ├── TablaSubDescuento (opcional, hasta 12)
│       ├── RecargoMonto (opcional)
│       ├── TablaSubRecargo (opcional, hasta 12)
│       ├── TablaImpuestoAdicional (opcional, hasta 2 por item)
│       ├── OtraMonedaDetalle (opcional)
│       └── MontoItem (obligatorio)
├── Subtotales (opcional, hasta 20)
├── DescuentosORecargos (opcional, hasta 20)
├── Paginacion (opcional, para facturas múltiples páginas)
├── InformacionReferencia (opcional, para facturas modificadas)
├── FechaHoraFirma (obligatorio)
└── [espacio para firma digital]
```

---

## 4. Cálculo de ITBIS

### Tipos de ITBIS

| Código | Tasa | Aplica a |
|--------|------|----------|
| ITBIS 1 | 18% | General |
| ITBIS 2 | 16% | Artículos de primera necesidad |
| ITBIS 3 | 0% | Gravado pero tasa 0% |
| Exento | 0% | No sujeto a ITBIS |

### Indicadores de Facturación por Item

| Valor | Descripción | ITBIS |
|-------|-------------|-------|
| 0 | No facturable (18% sobre margen) | 18% margen |
| 1 | ITBIS 18% | 18% |
| 2 | ITBIS 16% | 16% |
| 3 | ITBIS 3 (0% pero gravado) | 0% |
| 4 | Exento | 0% |

### Lógica de cálculo

```
MontoLinea = PrecioUnitario * Cantidad - DescuentoMonto + RecargoMonto

MontoGravadoI1 = Sum(Lineas con IndicadorFacturacion=1)
MontoGravadoI2 = Sum(Lineas con IndicadorFacturacion=2)
MontoGravadoI3 = Sum(Lineas con IndicadorFacturacion=3)
MontoExento = Sum(Lineas con IndicadorFacturacion=4)

TotalITBIS1 = MontoGravadoI1 * 0.18
TotalITBIS2 = MontoGravadoI2 * 0.16
TotalITBIS3 = 0

TotalITBIS = TotalITBIS1 + TotalITBIS2 + TotalITBIS3
MontoTotal = MontoGravadoI1 + MontoGravadoI2 + MontoGravadoI3 + MontoExento + TotalITBIS + ImpuestosAdicionales
```

### IndicadorMontoGravado

- **0 (default):** Los montos en las líneas NO incluyen ITBIS. Se calcula arriba.
- **1:** Los montos en las líneas YA INCLUYE ITBIS. DGII debe desglosar.

---

## 5. Hash de Seguridad (CodigoSeguridadeCF)

El hash es un elemento crítico de seguridad del e-CF.

**Algoritmo (según documentación DGII):**
1. Tomar el XML completo (INCLUSO el elemento de firma digital vacío)
2. Calcular SHA256 del XML canonicalizado
3. Los primeros 6 caracteres del hash base64 = CodigoSeguridadeCF
4. Este código se inserta en el XML (en el espacio reservado para firma)
5. El XML resultante se firma digitalmente

**Importante:** El hash se calcula DESPUÉS de construir el XML pero ANTES de firmar.

---

## 6. Firma Digital (XML-DSig)

El e-CF debe estar firmado digitalmente con un certificado de la DGII.

**Proceso:**
1. Construir XML completo con CodigoSeguridadeCF
2. Aplicar firma XML-DSig (W3C standard) con certificado A1 o A3
3. La firma cubre todo el documento (SignatureValue)
4. El XML firmado se envía a DGII

**Certificados disponibles:**
- **A1:** Archivo .p12/.pfx (privado + público) — más común para software
- **A3:** Hardware USB (sensor de huella o token) — más seguro

---

## 7. Envío a DGII

### API de DGII (según investigaciones)

La DGII tiene un servicio web para recepción de e-CF. Según el GitHub de vítors1681/dgii-ecf:

**Flujo:**
1. Crear XML e-CF 32
2. Calcular hash (6 chars)
3. Firmar XML digitalmente
4. Enviar XML + hash a DGII
5. Recibir TrackId
6. Polling para estado (Aceptado/Rechazado/EnProceso)

**TrackId response (ejemplo):**
```json
{
  "trackId": "d2b6e27c-3908-46f3-afaa-2207b9501b4b",
  "codigo": "1",
  "estado": "Aceptado",
  "rnc": "130862346",
  "encf": "E310005000201",
  "secuenciaUtilizada": true,
  "fechaRecepcion": "8/15/2023 6:06:57 AM",
  "mensajes": [{"valor": "", "codigo": 0}]
}
```

**Documentos de DGII disponibles:**
- Informe Técnico e-CF v1.0 (modificado 06/04/2026) — 1.7MB PDF
- Descripción Técnica Servicios Emisores Electrónicos (29/05/2026) — 1.6MB
- Descripción Técnica Servicios DGII (29/05/2026) — 2.3MB
- Todos los XSDs oficiales en dgii.gov.do

---

## 8. Contingencia (RFCE 32)

Cuando DGII no está disponible, se usa **RFCE 32 v.1.0.xsd** (15KB).

RFCE = Factura de consumo simplificada para contingencia.

**Características:**
- XML más simple (sin Todos los campos opcionales de e-CF)
- Mismo cálculo de ITBIS
- Se guarda localmente y se reenvía cuando DGII está disponible
- XSD: RFCE 32 v.1.0

---

## 9. Documentos asociados

| Documento | XSD | Función |
|-----------|-----|---------|
| e-CF 32 | e-CF 32 v.1.0.xsd | Factura de consumo principal |
| e-CF 31 | e-CF 31 v.1.0.xsd | Factura de crédito fiscal |
| ANECF | ANECF v.1.0.xsd | Anulación de NCF |
| ACECF | ACECF v.1.0.xsd | Aprobación comercial |
| ARECF | ARECF v1.0.xsd | Acuse de recibo |
| RFCE 32 | RFCE 32 v.1.0.xsd | Contingencia |
| Semilla | Semilla v.1.0.xsd | Contenedor base (valor + fecha + any) |

---

## 10. Referencias externas

- **Documentación oficial DGII:** https://dgii.gov.do/cicloContribuyente/facturacion/comprobantesFiscalesElectronicosE-CF/
- **GitHub de referencia (Node.js):** https://github.com/victors1681/dgii-ecf
- **Proveedor eCF MSeller:** https://ecf.mseller.app/ (API alternativa)
