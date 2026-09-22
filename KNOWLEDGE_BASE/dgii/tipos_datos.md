# DGII — XSDs y Tipos de Datos

**Fuente:** Análisis automático con DATACATALOG.PY + revisión manual
**Fecha:** 18/09/2026
**XSDs analizados:** 15 archivos

---

## 1. Resumen de XSDs

| Archivo | Tamaño | Tipos simples | Tipos complejos | Elementos | Propósito |
|---------|--------|---------------|-----------------|-----------|-----------|
| Semilla v.1.0.xsd | 487 B | 0 | 1 | 3 | Contenedor de autenticación (valor + fecha + any) |
| ACECF v.1.0.xsd | 3,567 B | 9 | 2 | 11 | Aprobación comercial |
| ANECF v.1.0.xsd | 5,160 B | 8 | 6 | 15 | Anulación de e-NCF |
| ARECF v1.0.xsd | 2,871 B | 6 | 2 | 9 | Acuse de recibo |
| RFCE 32 v.1.0.xsd | 15,325 B | 17 | 10 | 41 | Resumen factura consumo (<250K) |
| e-CF 31 v.1.0.xsd | 123,019 B | 73 | 38 | 237 | Factura crédito fiscal |
| e-CF 32 v.1.0.xsd | 122,998 B | 75 | 38 | 234 | Factura consumo **(PRINCIPAL)** |
| e-CF 33 v.1.0.xsd | 124,484 B | 75 | 39 | 244 | Nota débito |
| e-CF 34 v.1.0.xsd | 122,024 B | 71 | 37 | 236 | Nota crédito |
| e-CF 41 v.1.0.xsd | 111,171 B | 69 | 27 | 168 | Compras |
| e-CF 43 v.1.0.xsd | 96,111 B | 62 | 17 | 79 | Gastos menores |
| e-CF 44 v.1.0.xsd | 114,302 B | 72 | 35 | 180 | Regímenes especiales |
| e-CF 45 v.1.0.xsd | 121,785 B | 73 | 37 | 229 | Gubernamental |
| e-CF 46 v.1.0.xsd | 115,951 B | 77 | 29 | 196 | Exportaciones |
| e-CF 47 v.1.0.xsd | 101,382 B | 71 | 22 | 99 | Pagos al exterior |

---

## 2. Tipos Simples Críticos (e-CF 32)

### Enumeraciones principales

#### TipoeCFType (tipo de comprobante)
```
31 = Factura de Crédito Fiscal Electrónica
32 = Factura de Consumo Electrónica  ← POS
33 = Nota de Débito Electrónica
34 = Nota de Crédito Electrónica
41 = Compras Electrónico
43 = Gastos Menores Electrónico
44 = Regímenes Especiales Electrónico
45 = Gubernamental Electrónico
46 = Comprobante de Exportaciones Electrónico
47 = Comprobante para Pagos al Exterior Electrónico
```

#### IndicadorFacturacionType (ITBIS por línea)
```
0 = No facturable (18% sobre margen)
1 = ITBIS 1 (18%)
2 = ITBIS 2 (16%)
3 = ITBIS 3 (0% gravado)
4 = Exento (E)
```

#### IndicadorMontoGravadoType
```
0 = Montos sin ITBIS incluido (se calcula)
1 = Montos con ITBIS incluido (DGII desglosa)
```

#### FormaPagoType
```
1 = Efectivo
2 = Cheque/Transferencia/Depósito
3 = Tarjeta de Débito/Crédito
4 = Venta a Crédito
5 = Bonos o Certificados de regalo
6 = Permuta
7 = Nota de crédito
8 = Otras Formas de pago
```

#### TipoPagoType
```
1 = Contado
2 = Crédito
3 = Gratuito
```

#### TipoCuentaPagoType
```
CT = Cuenta Corriente
AH = Ahorro
OT = Otra
```

#### TipoMonedaType (monedas soportadas)
```
BRL, CAD, CHF, CHY, XDR, DKK, EUR, GBP, JPY, NOK, SCP, SEK, USD,
VEF, HTG, MXN, COP
```

#### CodificacionTipoImpuestosType (28 tipos de impuestos adicionales)
```
001 = Propina Legal
002 = Contribución Telecomunicaciones Ley 153-98
003 = Servicios Seguros
004 = Servicios de Telecomunicaciones
005 = Expedición primera placa
006-018 = Bebidas y Alcohol (cerveza, vinos, whisky, ron, etc.)
019-022 = Cigarrillos
023-027 = Bebidas y Alcohol (varios)
028-039 = Cigarrillos y otros
```

#### UnidadMedidaType (62 unidades)
```
1=Barrel, 2=Bolsa, 3=Bote, 4=Bulto, 5=Botella, 6=Caja,
7=Cajetilla, 8=cm, 9=Cilindro, 10=Conjunto, 11=Contenedor,
12=Día, 13=Docena, 14=Fardo, 15=Galón, 16=Grado, 17=Gramo,
18=Granel, 19=Hora, 20=Huacal, 21=Kg, 22=kWh, 23=Libra,
24=Litro, 25=Lote, 26=Metro, 27=M2, 28=M3, 29=MMBTU,
30=Minuto, 31=Paquete, 32=Par, 33=Pie, 34=Pieza, 35=Rollo,
36=Sobre, 37=Segundo, 38=Tanque, 39=Tonelada, 40=Tubo,
41=Yarda, 42=Yd2, 43=Unidad, 44=Elemento, 45=Millar,
46=Saco, 47=Lata, 48=Display, 49=Bidón, 50=Ración,
51=Quintal, 52=GRT, 53=P2, 54=Pasajero, 55=Pulgadas,
56=Parqueo Barcos, 57=Bandeja, 58=Hectárea, 59=Mililitro,
60=Miligramo, 61=Onzas, 62=Onzas Troy
```

#### IndicadorBienoServicioType
```
1 = Bien
2 = Servicio
```

#### TipoAfiliacionType
```
1 = Afiliada
2 = No afiliada
```

#### LiquidacionType
```
1 = Provisional
2 = Final
```

#### TipoDescuentoRecargoType
```
$ = Monto fijo
% = Porcentaje
```

#### TipoAjusteType
```
D = Descuento
R = Recargo
```

#### IndicadorFacturacionDRType (para descuentos/recargos)
```
1 = ITBIS 1 (18%)
2 = ITBIS 2 (16%)
3 = ITBIS 3 (0%)
4 = Exento
```

#### CodigoModificacionType
```
1 = Anula el NCF modificado
2 = Corrige texto del comprobante
3 = Corrige montos del NCF
4 = Reemplazo NCF en contingencia
5 = Referencia factura consumo electrónica
```

#### TipoIngresosValidationType
```
01 = Ingresos por operaciones (No financieros)
02 = Ingresos Financieros
03 = Ingresos Extraordinarios
04 = Ingresos por Arrendamientos
05 = Ingresos por Venta de Activo Depreciable
06 = Otros Ingresos
```

#### IndicadorNorma1007Type
```
0 = No incluir
1 = Incluir
```

#### IndicadorAgenteRetencionoPercepcionType
```
1 = Retención
2 = Percepción
```

#### EstadoRechazoType (ACECF)
```
0, 1, 2, 3, 4 (5 valores)
```

#### EstadoType
```
0, 1 (2 valores)
```

#### SerieType (ANECF - 25 series)
```
A-Z más combinaciones
```

---

## 3. Patrones de Validación

| Tipo | Patrón | Descripción |
|------|--------|-------------|
| eNCFValidationType | `([a-z0-9A-Z]{13})` | e-NCF: 13 chars alfanuméricos |
| RNCValidationType | `[0-9]{11}\|[0-9]{9}` | RNC: 9 o 11 dígitos |
| TelefonoValidationType | `\d{3}-\d{3}-\d{4}` | Teléfono: XXX-XXX-XXXX |
| CorreoValidationType | `\w+([-+.]\\w+)*@\w+([-.]\\w+)*\\.\\w+([-.]\\w+)*` | Email (max 80) |
| NumeroCuentaPagoType | min 1, max 28 | Número de cuenta (1-28 chars) |
| BancoPagoType | min 1, max 75 | Banco (1-75 chars) |
| NumeroLineaType | `[0-9]{0,4}`, 1-1000 | Número de línea (1-1000) |
| AlfNum40Type | Alfanumérico, 40 chars | Descripción corta |
| AlfNum45Type | Alfanumérico, 45 chars | Descripción media |
| AlfNum11a19ValidationType | 11-19 alfanuméricos | e-NCF modificado |
| FechaValidationType | Fecha DD-MM-AAAA | Fecha formato DGII |
| DateTimeValidationType | Fecha+hora | Fecha y hora firma |
| Integer2ValidationType | 0-99 | Entero 2 dígitos |
| Integer4V1To1000ValidationType | 1-1000 | Entero 4 dígitos (1-1000) |
| Decimal5D1or2ValidationTypeMayorCero | Decimal con 1-2 decimales, ≥0 | Montos pequeños |
| Decimal18D1or2ValidationTypeMayorIgualCero | Decimal 18 dígitos, 1-2 decimales, ≥0 | Montos grandes |
| Decimal18D1or2ValidationTypeMayorCero | Decimal 18 dígitos, 1-2 decimales, >0 | Montos >0 |
| Decimal20D1or4ValidationTypeMayorIgualCero | Decimal 20 dígitos, 1-4 decimales, ≥0 | Precios unitarios |
| TerminoPagoValidationType | `[\s\d\w]{1,15}` | Término de pago (1-15 chars) |

---

## 4. Provincias y Municipios (Destrucción del XSD)

El XSD contiene enumeration de todas las provincias y municipios de RD (670+ códigos). Algunos ejemplos:

| Código | Descripción |
|--------|-------------|
| 010000 | Distrito Nacional |
| 010100 | Municipio Santo Domingo de Guzmán |
| 010101 | Santo Domingo de Guzmán (D.M.) |
| 020000 | Provincia Azua |
| 020100 | Municipio Azua |
| 020101 | Azua (D.M.) |
| ... | ... |
| 190000 | Provincia Pedernales |
| 310000 | Provincia Peravia |

> Ver `KNOWLEDGE_BASE/dgii/municipios.md` para el listado completo.

---

## 5. Elementos Raíz por XSD

### e-CF 32 (elemento raíz: `ECF`)

La etiqueta raíz del XML debe ser `<ECF>` con tipo `EncabezadoECF32`.

### RFCE 32 (elemento raíz: `RFCE`)

La etiqueta raíz del XML debe ser `<RFCE>` con tipo `RFCE32`.

### ANECF (elemento raíz: `ANECF`)

### ACECF (elemento raíz: `ACECF`)

### ARECF (elemento raíz: `ARECF`)

---

## 6. Relación e-CF 32 → C# (recomendación de mapeo)

Basado en el análisis de XSD:

| Elemento XML | Tipo C# recomendado | Notas |
|-------------|---------------------|-------|
| ECF (root) | `ECF` class | Contenedor principal |
| Encabezado/ECF | `EncabezadoECF32` | Sección A |
| IdDoc | `IdDocECF32` | Identificación documento |
| TipoeCF | `int` (1-10) | 32 para POS |
| eNCF | `string` (13 chars) | Validar regex |
| IndicadorMontoGravado | `int` (0 o 1) | Default 0 |
| TipoPago | `int` (1-3) | Contado/Crédito/Gratuito |
| FechaLimitePago | `DateTime?` | Opcional |
| TablaFormasPago | `List<FormaPago>` | Max 7 |
| Emisor | `EmisorECF32` | Obligatorio |
| RNCEmisor | `string` (9/11 dígitos) | Validar RNC |
| Comprador | `CompradorECF32` | Obligatorio en e-CF 32 |
| Totales | `TotalesECF32` | Obligatorio |
| DetallesItems | `DetallesItemsECF32` | Items (max 1000) |
| Item | `ItemECF32` | Por línea |
| Subtotales | `SubtotalesECF32` | Opcional |
| DescuentosORecargos | `DescuentosORecargosECF32` | Opcional |
| Paginacion | `PaginacionECF32` | Opcional (multipagina) |
| InformacionReferencia | `InformacionReferenciaECF32` | Opcional |
| FechaHoraFirma | `DateTime` | Obligatorio |
| (espacio firma) | `object` | XML-DSig |

---

## 7. Archivos para profundizar

- `KNOWLEDGE_BASE/dgii/overview.md` — Visión general + estructura XML
- `KNOWLEDGE_BASE/dgii/api_rest.md` — API REST completa
- `KNOWLEDGE_BASE/dgii/municipios.md` — Listado de provincias/municipios
- `KNOWLEDGE_BASE/dgii/autoevaluacion.md` — Auto-evaluación de conocimiento
