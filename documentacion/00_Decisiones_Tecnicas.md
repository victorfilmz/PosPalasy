# Decisiones Técnicas — Sistema POS Facturación Electrónica DGII

**Versión:** 1.0  
**Fecha:** 16/09/2026  
**Estado:** Revisado y validado  

---

## 1. Introducción

Este documento registra las decisiones técnicas clave tomadas durante la planificación del módulo de facturación electrónica para el POS, documentando el "porqué" de cada decisión y garantizando trazabilidad para futuros desarrolladores y revisiones arquitectónicas.

---

## 2. Decisiones Estratégicas

### 2.1 Stack Tecnológico: .NET 10 + ASP.NET Core MVC + Clean Architecture

**Decisión:** Adoptar .NET 10, ASP.NET Core MVC, Clean Architecture, SQL Server, EF Core según el documento de arquitectura entregado.

**Por qué:**
- El documento de arquitectura original ya especificaba este stack, aprobado por el equipo
- .NET 10 ofrece las mejores herramientas para XML (XmlDocument, XDocument, XmlSchemaSet) esenciales para trabajar con XSDs de DGII
- Clean Architecture separa claramente tipos DGII (RNC, eNCF, etc.) en Domain, sin contaminar UI
- EF Core permite persistencia transaccional de facturas y datos relacionados

**Consecuencia:** La estructura de carpetas sigue Clean Architecture: Domain, Application, Infrastructure, WebAPI, UI

---

### 2.2 ValueObjects inmutables para tipos DGII (RNC, eNCF, FechaDominicana)

**Decisión:** Crear clases ValueObject inmutables para RNC, eNCF, FechaDominicana, con validación en constructor y operadores de igualdad.

**Por qué:**
- Los XSDs de DGII definen tipos estrictos: RNC (9/11 dígitos), eNCF (13 alfanuméricos), FechaDominicana (DD-MM-AAAA)
- Sin validación en el dominio, errores de formato llegan hasta la serialización XML
- La inmutabilidad previene modificaciones accidentales de datos fiscales
- Centraliza la validación: si DGII cambia el formato, solo se modifica el ValueObject

**Diseño:** 
- `RNC.cs`: valida 9 o 11 dígitos numéricos, gana ArgumentException si inválido
- `eNCF.cs`: valida 13 caracteres alfanuméricos con formato Serie+Secuencia
- `FechaDominicana.cs`: valida formato DD-MM-AAAA dominicano

**Estado:** ✅ Implementado en POS.Domain.Types

---

### 2.3 Proceso de Certificación DGII como prerrequisito

**Decisión:** Documentar y preparar el sistema para el proceso de certificación DGII antes de emitir facturas en producción.

**Por qué:**
- La DGII requiere certificación formal para todo emisor electrónico: inscripción RNC, estar al día tributariamente, certificado digital INDOTEL, formulario OFV, portal certificación
- Sin certificación, no se puede emitir e-CF legalmente en República Dominicana
- El sistema debe registrar las URLs de servicios requeridas durante certificación

**Riesgo de no hacerlo:** Producto no viable legalmente, no puede emitir facturas electrónicas

**Acción:** Sección de certificación incluida en todos los documentos técnicos

---

### 2.4 Integración con DGII: REST API JSON (no SOAP)

**Decisión:** Utilizar REST API con JSON para comunicación con servicios web de DGII.

**Por qué:**
- Investigación en dgii.gov.do reveló que servicios de recepción (`/fe/recepcion/api/ecf`), aprobación comercial (`/fe/aprobacioncomercial/api/ecf`), y autenticación (`/fe/autenticacion/api/[semilla]/[validacioncertificado]`) son REST API con JSON
- SOAP fue una suposición inicial incorrecta basada en prácticas antiguas de facturación electrónica
- DGII moderna usa REST + JSON: más simple de implementar con HttpClient
- Flujo con TrackId permite consulta asíncrona del resultado

**Formato request:** `{ "xml": "<base64 del XML>", "hash": "ABCD12" }`  
**Respuesta:** `{ "TrackId": "...", "Estado": "EnProceso" }`  
**Polling:** `GET /fe/recepcion/api/ecf/resultado/{trackId}`

**Corrección aplicada:** SOAP → REST API JSON en documentos 01, 02, 03, 04, 05, README.md

---

### 2.5 Firma digital XML-DSig con certificado INDOTEL

**Decisión:** Implementar firma digital XML-DSig para los e-CF usando certificado A1 o A3 de entidad autorizada por INDOTEL.

**Por qué:**
- DGII requiere que los e-CF estén firmados digitalmente
- Ley 32-23 y reglamento exigen certificado digital para procesos tributarios
- El XSD incluye `FechaHoraFirma` y el proceso de certificación valida la firma
- Sin firma digital, el e-CF no es válido ante DGII

**Decisión técnica:** Implementar XmlDigitalSigner con soporte A1 (.pfx) como prioridad; A3 (tarjeta inteligente) como opción adicional

**Estado:** ✅ Implementado XmlDigitalSigner.cs

---

### 2.6 Hash de Seguridad: SHA256 → 6 caracteres Base64

**Decisión:** Generar CodigoSeguridadeCF aplicando SHA256 al XML del e-CF y tomando los primeros 6 caracteres del resultado en base64.

**Por qué:**
- El XSD define CodigoSeguridadeCFType como cadena de 6 caracteres
- Este hash se envía junto con XML al servicio de recepción DGII
- Parte del mecanismo de integridad del e-CF
- SHA256 es el algoritmo estándar para este tipo de hash en facturación electrónica

**Implementación:** HashGenerator.cs calcula hash y formatea a 6 caracteres

---

## 3. Estructuras de Datos y Formatos

### 3.1 Generación de eNCF: Serie + Secuencia de 12 dígitos

**Decisión:** Generar eNCFs secuenciales con formato: `B` + 12 dígitos (ej: B0000000000001).

**Por qué:**
- El eNCF es único por emisor y no debe repetirse
- Los ejemplos de DGII muestran serie alfabética + secuencia numérica de 12 dígitos
- Serie "B" es la más común para facturas de consumo (e-CF 32)
- 12 dígitos permite hasta 1 billón de facturas por serie (suficiente para cualquier POS)
- Persistencia en BD permite recuperación tras caída del sistema

**Alternativas descartadas:**
- UUIDs: no cumplen formato requerido por DGII
- Generación aleatoria: riesgo de colisión

---

### 3.2 Estándares de Estado de Factura

**Decisión:** Utilizar los estados definidos por DGII: En Proceso (0), Aceptado (1), Rechazado (2), Anulado (3).

**Por qué:**
- DGII define los estados según resultado del servicio de recepción
- El sistema debe reflejar estos estados para mantenerse consistente con DGII
- Si DGII cambia los estados, sistema puede adaptarse fácilmente
- Evita crear estados propios que no correspondan con realidad fiscal

---

### 3.3 Contingencia: RFCE 32 para operación offline

**Decisión:** Implementar soporte para RFCE 32 como formato de contingencia cuando DGII no esté disponible.

**Por qué:**
- DGII provee el XSD RFCE 32 para facturación en modo contingencia
- Sin contingencia, el POS no podría facturar durante caídas de DGII
- RFCE 32 es versión simplificada del e-CF 32, válida para contingencia
- Sistema necesita tabla de facturas pendientes y mecanismo de reintento

**Alternativas descartadas:**
- Esperar a que DGII vuelva: inviable para negocio que necesita facturar
- Factura en papel: no cumple con ley 32-23

---

### 3.4 Almacenamiento de XMLs: Persistencia completa

**Decisión:** Almacenar el XML completo del e-CF generado en la base de datos para cada factura electrónica.

**Por qué:**
- Ley 32-23 exige conservación de comprobantes por 10 años
- El XML es la prueba oficial de la factura emitida
- Sin el XML original, no hay comprobación de lo enviado a DGII
- Auditoría tributaria puede requerir el XML exacto que se envió
- Hash (CodigoSeguridadeCF) permite verificar integridad del XML almacenado

---

### 3.5 Catálogo de ISC: 39 impuestos selectivos al consumo

**Decisión:** Implementar catálogo completo de ISC con los 39 códigos definidos en el XSD (001-039).

**Por qué:**
- XSD define códigos 001-039: específicos (montos fijos) y AdValorem (porcentajes)
- Códigos para bebidas alcohólicas, cigarrillos, telecomunicaciones, etc.
- Completar catálogo evita errores de validez para productos subject a ISC
- Código incorrecto hace inválido el e-CF según DGII
- Centralizar en clase static facilita mantenimiento

---

### 3.6 Unidades de Medida: 62 códigos según DGII

**Decisión:** Implementar enum completo de 62 unidades de medida definidas por DGII (1-62).

**Por qué:**
- XSD define unidades 1-62 con sus descripciones (incluye unidades muy específicas como MMBTU, Toneladas registro bruto, Parqueo barcos)
- Usar unidad incorrecta puede hacer inválido el e-CF
- Completar catálogo permite facturar cualquier tipo de producto
- POS puede mapear unidades internas a unidades DGII

---

### 3.7 Provincias y Municipios: Códigos de 6 dígitos

**Decisión:** Implementar ValueObject para códigos de provincia/municipio con formato 6 dígitos (PP0000 provincia, PPM000 municipio).

**Por qué:**
- XSD define ProvinciaMunicipioType: código de 6 dígitos
- Ejemplos: 010000 = Distrito Nacional, 010100 = Santo Domingo, 240000 = Santiago
- DGII tiene 61 provincias
- Código correcto requerido por XSD para dirección emisor/comprador
- Código inválido hace e-CF inválido según esquema XSD

---

### 3.8 Tipos de Moneda: DOP principal, USD/EUR secundarios

**Decisión:** Soportar DOP como moneda principal y USD/EUR para facturas en moneda extranjera.

**Por qué:**
- XSD TipoMonedaType define múltiples monedas (BRL, CAD, CHF, EUR, GBP, JPY, USD, etc.)
- Para POS en República Dominicana, monedas más relevantes: DOP, USD, EUR
- DOP es moneda de facto; USD y EUR cubren exportación o clientes extranjeros
- XSD permite hasta 17 tipos de moneda; implementar solo relevantes simplifica

---

### 3.9 Cálculo de ITBIS: 3 tasas (18%, 16%, 0%)

**Decisión:** Implementar cálculo de ITBIS basado en 3 tasas: 18% (general), 16% (primera necesidad), 0% (gravado sin ITBIS).

**Por qué:**
- DGII define tasas de ITBIS: general 18%, primera necesidad 16%, otros 0%
- Cada item tiene indicador de facturación que determina qué tasa aplica
- Cálculo sobre monto de línea (precio unitario × cantidad − descuentos + recargos)
- Tasas son fijas según ley; no deben configurarse arbitrary
- Cálculo debe ser preciso para evitar errores fiscales

---

## 4. Decisiones de Implementación

### 4.1 Serialización XML: XDocument/XElement (LINQ to XML)

**Decisión:** Usar XDocument y XElement de .NET para construir XML de e-CF.

**Por qué:**
- LINQ to XML es la API más productiva para construir XML en .NET
- Permite manejar elementos opcionales fácilmente (regresar null si no hay datos)
- Soporta namespaces y declaraciones XML correctamente
- XmlSerializer menos flexible para XSDs complejos

---

### 4.2 Validación XML: XmlSchemaSet + XmlReader

**Decisión:** Usar XmlSchemaSet + XmlReader con ValidationType.Schema para validar XML contra XSD.

**Por qué:**
- API estándar de .NET para validación XSD
- Permite cachear schemas para mejor rendimiento (XmlSchemaSet.Compile())
- Proporciona eventos de error y warning para diagnóstico detallado
- Más eficiente que validar con herramientas externas

---

### 4.3 HttpClient para communication con DGII

**Decisión:** Usar HttpClient para llamadas REST API a DGII.

**Por qué:**
- API estándar de .NET para HTTP
- Soporta timeout, cancellation, manejo JSON nativo
- Puede configurarse con base address y headers comunes
- Permite uso de HttpClientFactory para manejo de lifecycle

---

### 4.4 DTOs separados de entidades de dominio

**Decisión:** Crear DTOs específicos para solicitudes/respuestas de servicios, separados de entidades de dominio.

**Por qué:**
- DTOs de servicio (ElectronicInvoiceRequest) tienen estructura diferente a ValueObjects del dominio
- Facilita evolución de API sin afectar dominio
- Permite añadir campos específicos de serialización (ej: Totales con campos calculados)
- Separa preocupaciones: dominio = reglas, DTO = transferencia de datos

---

### 4.5 Repository Pattern

**Decisión:** Implementar repositorio para persistencia de facturas electrónicas (IInvoiceRepository + SqlInvoiceRepository).

**Por qué:**
- Permite cambiar implementación de persistencia sin afectar lógica de aplicación
- Facilita testing con repositorios en memoria (mocks)
- Separa lógica de negocio de detalles de acceso a SQL Server
- Soporta futuras ampliaciones (consultas complejas, caching)

---

### 4.6 Command/Handler para casos de uso

**Decisión:** Usar patrón Command/Handler para casos de uso de facturación electrónica.

**Por qué:**
- Cada caso de uso (EmitirFactura, AnularFactura, ConsultarEstado) es un command separado
- Los handlers orquesan la lógica: obtener venta, generar request, validar, enviar, persistir
- Facilita testing unitario de cada caso de uso de forma aislada
- Se integra bien con MediatR si se decide usarlo en futuro

**Commands definidos:**
- `EmitirFacturaCommand` → `EmitirFacturaHandler`
- `AnularFacturaCommand` → `AnularFacturaHandler`
- `ConsultarEstadoFacturaCommand` → `ConsultarEstadoFacturaHandler`
- `EnviarAnulacionCommand` → `EnviarAnulacionHandler`

---

### 4.7 Validación FluentValidation

**Decisión:** Usar FluentValidation para validar requests de facturación antes de enviar a DGII.

**Por qué:**
- Permite definir reglas de validación declarativamente
- Centraliza validación en una clase por command
- Integra bien con pipeline de comandos
- Alternativa a validaciones manuales dispersas

**Validators planeados:**
- `EmitirFacturaValidator`: valida request de emisión
- `AnulacionValidator`: valida request de anulación

---

### 4.8 Logging con ILogger para auditoría

**Decisión:** Implementar logging extensivo de toda la comunicación con DGII.

**Por qué:**
- Necesario para debugging de problemas de integración con DGII
- Requerido para auditoría tributaria (qué se envió, cuándo, resultado)
- Ayuda a identificar patrones de error y tomar decisiones operativas
- Logging debe incluir: TrackId, eNCF, timestamps, estado, errores

---

### 4.9 Configuración mediante IConfiguration (appsettings.json)

**Decisión:** Usar sistema de configuración de .NET para almacenar configuraciones de DGII y empresa.

**Por qué:**
- Permite cambiar configuración sin recompilar
- Soporta diferentes configuraciones por ambiente (development, production)
- Se puede usar User Secrets para datos sensibles en desarrollo
- Estructura clara: DGII, Enterprise, Tax sections

---

## 5. Decisiones de Testing

### 5.1 Testing por capas

**Decisión:** Implementar tests unitarios por capas.

**Por qué:**
- Domain: probar que RNC/eNCF/fecha validan correctamente
- Application: probar que cálculos de ITBIS son correctos
- Infrastructure: probar que serialización XML produce XML válido contra XSD
- Tests de integración para flujo completo (sale → factura → DGII)

---

### 5.2 Uso de DGII Sandbox para pruebas de integración

**Decisión:** Cuando esté disponible, usar sandbox de DGII para pruebas de integración reales.

**Por qué:**
- Pruebas con datos reales DGII son necesarias para validar integración completa
- Sandbox permite testing sin afectar datos de producción
- Se debe documentar flujo de prueba (envío, polling, recepción ARECF/ACECF)

**Estado:** ⏳ Sandbox DGII a confirmar

---

## 6. Decisiones Pendientes / Para Revisar

### 6.1 Endpoints exactos de DGII

**Estado:** ⏳ Pendiente de confirmación  
**Riesgo:** Los endpoints exactos pueden variar según ambiente (producción vs sandbox)  
**Acción:** Confirmar con DGII o con proveedor de facturación electrónica

### 6.2 Certificado digital A1 vs A3

**Estado:** ⏳ Pendiente de decisión  
**Riesgo:** A3 requiere hardware adicional (lector de tarjetas)  
**Acción:** Evaluar costos y conveniencia; A1 es más fácil de implementar inicialmente

### 6.3 Firma XML-DSig avanzada (múltiples referencias, transformaciones)

**Estado:** ⏳ Pendiente de definición técnica  
**Riesgo:** La firma básica puede no cumplir con requisitos específicos de DGII  
**Acción:** Validar con DGII o proveedor certificado el formato de firma requerido

### 6.4 QR Code para facturas

**Estado:** ⏳ Pendiente de implementación  
**Riesgo:** DGII puede exigir formato específico de QR  
**Acción:** Confirmar especificaciones de QR con DGII

### 6.5 Módulo de recepción de e-CF (cuando el POS actúa como receptor)

**Estado:** ⏳ Fuera de alcance inicial  
**Contexto:** DGII indica que el contribuyente puede ser receptor de e-CF (ej: compras de proveedores)  
**Acción:** Evaluar si POS necesita recibir e-CF de proveedores o solo emitir

---

## 7. Referencias DGII

- **Ley No. 32-23 de Facturación Electrónica:** dgii.gov.do/legislacion/leyesTributarias/Documents/Otras%20Leyes%20de%20Interés/32-23.pdf
- **Decreto No. 587-24 (Reglamento):** dgii.gov.do
- **Norma General No. 01-2020:** Regula emisión y uso de e-CF
- **Portal de Certificación:** dgii.gov.do/cicloContribuyente/facturacion/comprobantesFiscalesElectronicosE-CF/
- **Proceso de Certificación:** dgii.gov.do/cicloContribuyente/facturacion/comprobantesFiscalesElectronicosE-CF/Documentación sobre eCF/Documentaciones Proceso de Certificación FE/
- **Guía para ser Emisor Electrónico:** dgii.gov.do/publicacionesOficiales/bibliotecaVirtual/contribuyentes/facturacion/Documents/Facturación Electrónica/Guia-Basica-para-ser-Emisor-Electronico.pdf
- **XSDs de DGII:** Carpeta `documentacion xsd/` del proyecto

---

## 8. Resumen de Correcciones Realizadas (SOAP → REST API JSON)

### 8.1 Documento 01_Resumen_General.md
- Sección "8. Flujo de Envío a DGII" reescrita completa: SOAP → REST API JSON con TrackId
- Añadido calendario de implementación (ley 32-23, decreto 587-24)
- Añadido proceso de certificación (5 pasos hasta portal certificación)

### 8.2 Documento 02_Mapeo_XSD_CSharp.md
- Sección "18. Integración con DGII" reescrita: SOAP → REST API JSON
- Flujo de emisión: pasos 9-12 → pasos 9-13 (envío, TrackId, polling, recepción acuses)
- Endpoint URL corregido: `https://dfe.dgii.gov.do/fe/recepcion/api/ecf`

### 8.3 Documento 03_Interfaces_Servicios.md
- Diagramas secuencia: SOAP → REST API JSON
- Sección "8. Flujo de Envío a DGII": SOAP → REST API JSON

### 8.4 Documento 04_Diseno_Infraestructura_DGII.md
- Implementación de envío SOAP → REST API JSON
- DgiiApiClient: SOAP → REST API JSON con HttpClient

### 8.5 Documento 05_Plan_Implementacion.md
- Sección "3.5 DgiiElectronicInvoiceService": SOAP → REST API JSON
- Pasos del flujo: SOAP → REST API JSON con TrackId

### 8.6 Documento README.md
- Flujo actualizado: "Enviar a DGII (SOAP/HTTP)" → "Enviar a DGII (REST API JSON con TrackId)"

---

## 9. Correcciones de Sintaxis en ValueObjects

### 9.1 RNC.cs
- **Error:** `booloperator ==` y `booloperator !=` (sin espacio entre `bool` y `operator`)
- **Corrección:** `bool operator ==` y `bool operator !=`

### 9.2 eNCF.cs
- **Error:** `booloperator ==` y `booloperator !=` (sin espacio entre `bool` y `operator`)
- **Corrección:** `bool operator ==` y `bool operator !=`

---

## 10. Conclusiones

Todas las decisiones documentadas en este documento son consistentes con:

1. **Normativa DGII vigente:** Ley 32-23, Decreto 587-24, Norma General 01-2020
2. **Esquemas XSD de DGII:** Todas las clases mapean exactamente los tipos definidos en los XSDs
3. **Stack tecnológico:** .NET 10 / ASP.NET Core MVC / Clean Architecture / SQL Server / EF Core
4. **Prácticas de desarrollo:** Inmutabilidad, validación temprana, separación de responsabilidades, testing

Las decisiones pendientes (endpoints exactos, certificado A1 vs A3, firma XML-DSig avanzada, QR Code, recepción e-CF) requieren confirmación con DGII o con proveedor certificado antes de su implementación.

**Este documento debe actualizarse cada vez que se tome una decisión técnica significativa que afecte el diseño del sistema.**
