# Plan de Implementación — Módulo de Facturación Electrónica DGII

**Versión:** 1.0  
**Fecha:** 16/09/2026  
**Stack:** .NET 10 / ASP.NET Core MVC / Clean Architecture  
**Estado:** Borrador — Pendiente de revisión

---

## 1. Resumen Ejecutivo

Este plan detalla la implementación del módulo de facturación electrónica para el POS, siguiendo la arquitectura Clean Architecture definida previamente. El módulo permite emitir facturas electrónicas conforme a los esquemas XSD de la DGII dominicana, manejar anulaciones, consultar estados, y operar en modo contingencia cuando DGII no está disponible.

**Duración estimada:** 19 semanas (equipo completo: 1 desarrollador full-time + 1 QA + 1 PO)  
**Duración estimada (1-2 devs):** 24-30 semanas (~6-7 meses)

---

## 2. Fases del Proyecto

### Fase 1: Infraestructura Básica (2 semanas)

**Objetivo:** Crear los bloques fundamentales sobre los cuales se construirá el módulo completo.

#### 1.1 Crear Proyecto de Dominio de Tipos

```
POS.Domain.Types/
├── RNC.cs
├── eNCF.cs
├── FechaDominicana.cs
├── VersionType.cs
├── TipoeCFType.cs
├── TipoIngresosType.cs
├── TipoPagoType.cs
├── FormaPagoType.cs
├── IndicadorFacturacionType.cs
├── IndicadorBienoServicioType.cs
├── UnidadMedidaType.cs
├── ProvinciaMunicipio.cs
├── ImpuestoAdicional.cs
├── TipoMoneda.cs
├── DecimalTypes.cs
│   ├── DecimalMayorIgualCero.cs
│   ├── DecimalMayorCero.cs
│   ├── Decimal18D2.cs
│   ├── Decimal20D1or4MayorIgualCero.cs
│   ├── Decimal19D1or3MayorIgualCero.cs
│   ├── Decimal7D1or4MayorCero.cs
│   ├── Decimal5D1or2MayorCero.cs
│   ├── DecimalMontoNegativoPositivo.cs
│   └── DecimalMayorCeroPagina.cs
│
├── ComplexTypes/
│   ├── EncabezadoECF32.cs
│   ├── EmisorECF32.cs
│   ├── CompradorECF32.cs
│   ├── TotalesECF32.cs
│   ├── TransporteECF32.cs
│   ├── InformacionesAdicionalesECF32.cs
│   ├── OtraMonedaECF32.cs
│   ├── DetallesItemsECF32.cs
│   ├── ItemECF32.cs
│   ├── CodigosItemECF32.cs
│   ├── SubcantidadItemECF32.cs
│   ├── MineriaItemECF32.cs
│   ├── SubDescuentoItemECF32.cs
│   ├── SubRecargoItemECF32.cs
│   ├── ImpuestoAdicionalItemECF32.cs
│   ├── OtraMonedaDetalleItemECF32.cs
│   ├── SubtotalesECF32.cs
│   ├── DescuentosORecargosECF32.cs
│   ├── DescuentoORecargoECF32.cs
│   ├── PaginacionECF32.cs
│   ├── PaginaECF32.cs
│   ├── InformacionReferenciaECF32.cs
│   ├── ANECF.cs
│   ├── ACECF.cs
│   └── ARECF.cs
│
├── Enums/
│   ├── CFType.cs
│   ├── EstadoType.cs
│   ├── EstadoAcuseType.cs
│   ├── CodigoMotivoNoRecibidoType.cs
│   ├── CodigoModificacionType.cs
│   ├── EstadoFactura.cs
│   ├── EstadoAprobacion.cs
│   ├── TipoAfiliacionType.cs
│   └── LiquidacionType.cs
│
└── Formatters/
    ├── TipoIngresosFormatter.cs
    ├── ImpuestoAdicionalFormatter.cs
    ├── MonedaFormatter.cs
    ├── UnidadMedidaFormatter.cs
    └── ProvinciaMunicipioFormatter.cs
```

**Tareas:**
- [ ] Crear proyecto POS.Domain.Types como librería de clases .NET 10
- [ ] Implementar todos los ValueObjects de tipos simples
- [ ] Implementar todas las clases de tipos complejos
- [ ] Implementar enumeraciones completas
- [ ] Implementar formateadores y conversores
- [ ] Añadir pruebas unitarias para cada ValueObject
- [ ] Añadir validaciones en cada constructor
- [ ] Publicar como paquete interno o referenciar directamente

#### 1.2 Configuración del Proyecto POS

```
POS/
├── src/
│   ├── POS.Domain/
│   ├── POS.Domain.Types/      ← nuevo
│   ├── POS.Application/
│   ├── POS.Application.Interfaces/  ← nuevo
│   ├── POS.Infrastructure/
│   │   ├── POS.Infrastructure.ElectronicInvoicing/  ← nuevo
│   │   └── POS.Infrastructure.Persistence/
│   └── POS.Web/
└── tests/
    ├── POS.Domain.Types.Tests/  ← nuevo
    ├── POS.Application.Tests/
    └── POS.Infrastructure.Tests/
```

**Tareas:**
- [ ] Crear proyecto POS.Domain.Types como librería .NET 10
- [ ] Crear proyecto POS.Application.Interfaces como librería .NET 10
- [ ] Crear proyecto POS.Infrastructure.ElectronicInvoicing como librería .NET 10
- [ ] Configurar referencias entre proyectos
- [ ] Configurar soluciones de prueba

---

### Fase 2: Contratos de Interfaces (3 semanas)

**Objetivo:** Definir los contratos que separan completamente la aplicación de la infraestructura.

#### 2.1 Interfaces en POS.Application.Interfaces

```
POS.Application.Interfaces/
├── ElectronicInvoicing/
│   ├── IElectronicInvoiceService.cs
│   ├── IElectronicInvoiceRepository.cs
│   ├── IInvoiceRequestGenerator.cs
│   ├── IXmlValidator.cs
│   ├── IXmlSerializer.cs
│   ├── IHashGenerator.cs
│   ├── IDigitalSignatureService.cs
│   ├── IInvoiceQueryService.cs
│   ├── IContingencyService.cs
│   └── ISaleRepository.cs
│
├── DTOs/
│   ├── ElectronicInvoiceRequest.cs
│   ├── ElectronicInvoiceResult.cs
│   ├── ElectronicInvoiceStatusResponse.cs
│   ├── AnulacionRequest.cs
│   ├── AnulacionResult.cs
│   ├── RangoAnulacionRequest.cs
│   ├── RangoSecuenciaRequest.cs
│   ├── EmisorRequest.cs
│   ├── CompradorRequest.cs
│   ├── FormaDePagoRequest.cs
│   ├── InvoiceLineRequest.cs
│   ├── InvoiceCodeRequest.cs
│   ├── InvoiceSubDiscountRequest.cs
│   ├── InvoiceSubRecargoRequest.cs
│   ├── InvoiceSubtotalRequest.cs
│   ├── InvoiceDiscountRequest.cs
│   ├── InvoicePageRequest.cs
│   ├── InvoiceReferenceRequest.cs
│   ├── InvoiceOtherCurrencyRequest.cs
│   ├── InvoiceOtherCurrencyTaxRequest.cs
│   ├── InvoiceSummaryDTO.cs
│   ├── InvoiceListItemDTO.cs
│   ├── InvoiceDetailDTO.cs
│   ├── AcuseReciboDTO.cs
│   └── AprobacionComercialDTO.cs
│
├── Enums/
│   ├── ElectronicInvoiceStatus.cs (copiado desde Domain.Types o aquí)
│   ├── EstadoAcuse.cs
│   └── EstadoAprobacion.cs
│
└── Exceptions/
    ├── InvoiceException.cs
    ├── SaleNotFoundException.cs
    ├── InvoiceNotFoundException.cs
    ├── InvoiceAlreadyExistsException.cs
    ├── InvoiceNotApprovedException.cs
    ├── InvoiceValidationException.cs
    ├── InvoiceSubmissionException.cs
    ├── InvoiceAnulacionException.cs
    ├── DGIIException.cs
    ├── CertificateException.cs
    └── XmlValidationException.cs
```

**Tareas:**
- [ ] Definir interfaz IElectronicInvoiceService
- [ ] Definir interfaz IElectronicInvoiceRepository
- [ ] Definir interfaz IInvoiceRequestGenerator
- [ ] Definir interfaz IXmlValidator
- [ ] Definir interfaz IXmlSerializer
- [ ] Definir interfaz IHashGenerator
- [ ] Definir interfaz IDigitalSignatureService
- [ ] Definir interfaz IInvoiceQueryService
- [ ] Definir interfaz IContingencyService
- [ ] Definir interfaz ISaleRepository
- [ ] Definir todos los DTOs de request/response
- [ ] Definir excepciones personalizadas
- [ ] Añadir pruebas unitarias para validar interfaces

---

### Fase 3: Implementación de Infraestructura DGII (4 semanas)

**Objetivo:** Implementar los servicios concretos que interactúan con DGII y los XSDs.

#### 3.1 XmlSerializer

```
POS.Infrastructure.ElectronicInvoicing/
├── XmlSerializer.cs
├── XmlSerializer.ECF32.cs     ← métodos específicos e-CF 32
├── XmlSerializer.ECF31.cs     ← métodos específicos e-CF 31
├── XmlSerializer.RFCE32.cs    ← métodos específicos RFCE 32
├── XmlSerializer.ANECF.cs
├── XmlSerializer.ACECF.cs
├── XmlSerializer.ARECF.cs
└── HelperMethods/
    ├── XElementHelpers.cs
    ├── OpcionalElement.cs
    └── EnumToString.cs
```

**Tareas:**
- [ ] Implementar serialización e-CF 32 completa
- [ ] Implementar serialización e-CF 31
- [ ] Implementar serialización RFCE 32 (contingencia)
- [ ] Implementar serialización ANECF
- [ ] Implementar serialización ACECF
- [ ] Implementar serialización ARECF
- [ ] Implementar métodos helper para elementos opcionales
- [ ] Implementar helper para telefonos, impuestos, etc.
- [ ] Añadir pruebas unitarias para cada tipo de serialización
- [ ] Validar XMLs generados contra XSDs correspondientes

#### 3.2 XmlValidator

```
POS.Infrastructure.ElectronicInvoicing/
├── XmlValidator.cs
└── SchemaCache.cs
```

**Tareas:**
- [ ] Implementar XmlValidator con caché de schemas
- [ ] Validar XML contra XSD e-CF 32
- [ ] Validar XML contra XSD e-CF 31
- [ ] Validar XML contra XSD RFCE 32
- [ ] Validar XML contra XSD ANECF
- [ ] Validar XML contra XSD ACECF
- [ ] Validar XML contra XSD ARECF
- [ ] Añadir pruebas unitarias para validación
- [ ] Mejorar manejo de errores y propagación de errores

#### 3.3 HashGenerator

```
POS.Infrastructure.ElectronicInvoicing/
└── HashGenerator.cs
```

**Tareas:**
- [ ] Implementar HashGenerator (SHA256 → 6 chars base64)
- [ ] Comprobar que mismo XML produce mismo hash
- [ ] Comprobar que XML diferente produce hash diferente
- [ ] Añadir pruebas unitarias

#### 3.4 DigitalSignatureService

```
POS.Infrastructure.ElectronicInvoicing/
├── XmlDigitalSigner.cs
└── NoOpDigitalSignatureService.cs (para desarrollo sin certificado)
```

**Tareas:**
- [ ] Implementar XmlDigitalSigner con firma XML-DSig
- [ ] Implementar NoOpDigitalSignatureService para desarrollo
- [ ] Probar con certificado A1 de prueba
- [ ] Integrar con configuración de certificado

#### 3.5 DgiiElectronicInvoiceService (CORRECTO - DGII REST API JSON)

```csharp
POS.Infrastructure.ElectronicInvoicing/
├── DgiiElectronicInvoiceService.cs    ← implementa IElectronicInvoiceService
├── DgiiApiClient.cs                   ← REST API Client para DGII (JSON)
├── DgiiResponse.cs                    ← DTOs de respuesta DGII
├── AnulacionDgiiResponse.cs           ← DTOs de anulación
├── RetryHandler.cs                    ← Manejo reintentos
└── DgiiClient.cs                      ← manipulación HTTP (opcional)
```

**Flujo de envío correcto (según DGII):**
```
1. Serializar factura → XML e-CF 32 (XmlSerializer)
2. Validar XML contra XSD (XmlValidator)
3. Calcular Hash (CodigoSeguridadeCF - SHA256 → 6 chars)
4. Firmar XML digitalmente (XmlDigitalSigner - certificado A1/A3)
5. Enviar a DGII vía REST API JSON:
   POST /fe/recepcion/api/ecf
   Body: { "xml": "<base64 del XML>", "hash": "ABCD12" }
6. DGII responde con TrackId y estado inicial
7. Polling con TrackId para consultar resultado:
   GET /fe/recepcion/api/ecf/resultado/{trackId}
   Estados: EnProceso / Aceptado / Rechazado / Anulado
8. Recibir ARECF (acuse inmediato) y ACECF (aprobación)
9. Persistir resultados en BD
```

**Tareas:**
- [ ] Implementar SubmitAsync con DGiiApiClient (REST API JSON)
- [ ] Implementar envío POST /fe/recepcion/api/ecf con JSON {xml, hash}
- [ ] Implementar recepción TrackId y estado inicial
- [ ] Implementar polling GET /fe/recepcion/api/ecf/resultado/{trackId}
- [ ] Implementar recepción ARECF y ACECF de DGII
- [ ] Implementar retry logic con backoff exponencial
- [ ] Implementar manejo de timeout y errores de red
- [ ] Implementar AnularAsync (XML ANECF + REST API)
- [ ] Implementar ConsultarEstadoAsync (GET resultado con TrackId)
- [ ] Implementar GenerateXmlAsync (serialización XML)
- [ ] Implementar GenerateAnulacionXmlAsync (serialización ANECF)
- [ ] Añadir logging extensivo de toda comunicación con DGII
- [ ] Integrar con appsettings para configuración DGII
- [ ] Añadir manejo de contingencia automático (RFCE 32)
- [ ] Añadir pruebas de integración con DGII (cuando disponible)

---

### Fase 4: Integración con Base de Datos (2 semanas)

**Objetivo:** Persistir facturas electrónicas, items, y datos relacionados en SQL Server.

#### 4.1 Tablas en SQL Server

```sql
CREATE TABLE ElectronicInvoices (
    Id INT IDENTITY PRIMARY KEY,
    SaleId INT NOT NULL,
    TipoeCF INT NOT NULL,
    eNCF VARCHAR(13) NOT NULL UNIQUE,
    Version DECIMAL(2,1) NOT NULL,
    RNCEmisor VARCHAR(11) NOT NULL,
    RNCComprador VARCHAR(11) NULL,
    RazonSocialComprador VARCHAR(150) NULL,
    FechaEmision DATE NOT NULL,
    TipoPago INT NOT NULL,
    TipoIngresos CHAR(2) NOT NULL,
    FechaLimitePago DATE NULL,
    TerminoPago VARCHAR(15) NULL,
    TipoCuentaPago CHAR(2) NULL,
    NumeroCuentaPago VARCHAR(28) NULL,
    BancoPago VARCHAR(75) NULL,
    IndicadorEnvioDiferido INT NULL,
    IndicadorMontoGravado INT NULL,
    IndicadorServicioTodoIncluido INT NULL,
    MontoGravadoTotal DECIMAL(18,2) NOT NULL,
    MontoGravadoI1 DECIMAL(18,2) NOT NULL,
    MontoGravadoI2 DECIMAL(18,2) NOT NULL,
    MontoGravadoI3 DECIMAL(18,2) NOT NULL,
    MontoExento DECIMAL(18,2) NOT NULL,
    ITBIS1 INT NULL,
    ITBIS2 INT NULL,
    ITBIS3 INT NULL,
    TotalITBIS DECIMAL(18,2) NOT NULL,
    TotalITBIS1 DECIMAL(18,2) NOT NULL,
    TotalITBIS2 DECIMAL(18,2) NOT NULL,
    TotalITBIS3 DECIMAL(18,2) NOT NULL,
    MontoImpuestoAdicional DECIMAL(18,2) NULL,
    MontoTotal DECIMAL(18,2) NOT NULL,
    XMLContent NVARCHAR(MAX) NOT NULL,
    XMLHash CHAR(6) NOT NULL,
    Estado INT NOT NULL,
    FechaEnvio DATETIME NOT NULL,
    FechaAprobacion DATETIME NULL,
    FechaAnulacion DATETIME NULL,
    MotivoRechazo NVARCHAR(250) NULL,
    MotivoAnulacion NVARCHAR(250) NULL,
    ARECFXML NVARCHAR(MAX) NULL,
    ACECFXML NVARCHAR(MAX) NULL,
    CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_ElectronicInvoices_Sales FOREIGN KEY (SaleId) REFERENCES Sales(Id)
);

CREATE TABLE ElectronicInvoiceItems (
    Id INT IDENTITY PRIMARY KEY,
    ElectronicInvoiceId INT NOT NULL,
    NumeroLinea INT NOT NULL,
    IndicadorFacturacion INT NOT NULL,
    IndicadorBienoServicio INT NOT NULL,
    NombreItem VARCHAR(80) NOT NULL,
    DescripcionItem VARCHAR(1000) NULL,
    CantidadItem DECIMAL(18,2) NOT NULL,
    UnidadMedida INT NULL,
    CantidadReferencia DECIMAL(18,2) NULL,
    UnidadReferencia INT NULL,
    PrecioUnitarioItem DECIMAL(20,4) NOT NULL,
    DescuentoMonto DECIMAL(18,2) NULL,
    RecargoMonto DECIMAL(18,2) NULL,
    MontoItem DECIMAL(18,2) NOT NULL,
    GradosAlcohol DECIMAL(5,2) NULL,
    FechaElaboracion DATE NULL,
    FechaVencimientoItem DATE NULL,
    FOREIGN KEY (ElectronicInvoiceId) REFERENCES ElectronicInvoices(Id) ON DELETE CASCADE
);

CREATE TABLE ElectronicInvoiceContingency (
    Id INT IDENTITY PRIMARY KEY,
    SaleId INT NOT NULL,
    TipoeCF INT NOT NULL,
    eNCF VARCHAR(13) NOT NULL,
    Version DECIMAL(2,1) NOT NULL,
    RNCEmisor VARCHAR(11) NOT NULL,
    RNCComprador VARCHAR(11) NULL,
    RazonSocialComprador VARCHAR(150) NULL,
    FechaEmision DATE NOT NULL,
    TipoPago INT NOT NULL,
    TipoIngresos CHAR(2) NOT NULL,
    XMLContent NVARCHAR(MAX) NOT NULL,
    XMLHash CHAR(6) NOT NULL,
    FechaGuardado DATETIME NOT NULL,
    Reintentos INT NOT NULL DEFAULT 0,
    UltimoError NVARCHAR(500) NULL,
    FechaProximoReintento DATETIME NULL
);

CREATE INDEX IX_ElectronicInvoices_eNCF ON ElectronicInvoices(eNCF);
CREATE INDEX IX_ElectronicInvoices_Estado ON ElectronicInvoices(Estado);
CREATE INDEX IX_ElectronicInvoices_FechaEmision ON ElectronicInvoices(FechaEmision);
CREATE INDEX IX_ElectronicInvoices_RNC ON ElectronicInvoices(RNCEmisor);
CREATE INDEX IX_ElectronicInvoiceItems_ElectronicInvoiceId ON ElectronicInvoiceItems(ElectronicInvoiceId);
```

#### 4.2 EF Core Mappings

```
POS.Infrastructure.Persistence/
├── ApplicationDbContext.cs
├── Configurations/
│   ├── ElectronicInvoiceConfiguration.cs
│   └── ElectronicInvoiceItemConfiguration.cs
├── Entities/
│   ├── ElectronicInvoiceEntity.cs
│   └── ElectronicInvoiceItemEntity.cs
└── Repositories/
    ├── SqlInvoiceRepository.cs
    └── SqlSaleRepository.cs
```

**Tareas:**
- [ ] Crear ApplicationDbContext con DbSet para ElectronicInvoices, ElectronicInvoiceItems, ElectronicInvoiceContingency
- [ ] Configurar mappings EF Core con Fluent API
- [ ] Crear migraciones EF Core
- [ ] Implementar SqlInvoiceRepository (Full CRUD para facturas)
- [ ] Implementar SqlSaleRepository (lectura de ventas)
- [ ] Implementar repositorio de contingencia
- [ ] Pruebas de integración con base de datos real

---

### Fase 5: Casos de Uso de Aplicación (3 semanas)

**Objetivo:** Implementar los command handlers que orquestan el flujo completo de facturación.

```
POS.Application/
├── ElectronicInvoicing/
│   ├── Commands/
│   │   ├── EmitirFacturaCommand.cs
│   │   ├── EmitirFacturaCommandHandler.cs
│   │   ├── AnularFacturaCommand.cs
│   │   ├── AnularFacturaCommandHandler.cs
│   │   ├── ConsultarEstadoFacturaCommand.cs
│   │   ├── ConsultarEstadoFacturaCommandHandler.cs
│   │   ├── GenerarXmlFacturaCommand.cs
│   │   ├── GenerarXmlFacturaCommandHandler.cs
│   │   └── GenerarXmlAnulacionCommand.cs
│   │   └── GenerarXmlAnulacionCommandHandler.cs
│   │
│   ├── Validators/
│   │   └── EmitirFacturaValidator.cs
│   │
│   └── RequestGenerators/
│       └── InvoiceRequestGenerator.cs
│
└── Queries/
    ├── GetInvoiceSummaryQuery.cs
    ├── GetInvoiceSummaryQueryHandler.cs
    ├── GetInvoiceListQuery.cs
    ├── GetInvoiceListQueryHandler.cs
    ├── GetInvoiceDetailQuery.cs
    └── GetInvoiceDetailQueryHandler.cs
```

**Tareas:**
- [ ] Implementar InvoiceRequestGenerator (Sale → ElectronicInvoiceRequest)
- [ ] Implementar EmitirFacturaCommand + Handler
- [ ] Implementar AnularFacturaCommand + Handler
- [ ] Implementar ConsultarEstadoFacturaCommand + Handler
- [ ] Implementar GenerarXmlFacturaCommand + Handler
- [ ] Implementar GenerarXmlAnulacionCommand + Handler
- [ ] Implementar query handlers para consultas
- [ ] Implementar validación con FluentValidation
- [ ] Manejo de errores y excepciones
- [ ] Añadir pruebas unitarias para cada command handler
- [ ] Añadir pruebas de integración con repositorios

---

### Fase 6: Integración con UI (2 semanas)

**Objetivo:** Crear las vistas y controles de MVC para interacción con el usuario.

```
POS.Web/
├── Controllers/
│   └── FacturacionElectronicaController.cs
│
├── Views/
│   └── FacturacionElectronica/
│       ├── Configuracion.cshtml
│       ├── Historial.cshtml
│       ├── Detalle.cshtml
│       ├── Estado.cshtml
│       └── Reintentar.cshtml
│
├── ViewModels/
│   ├── FacturacionConfigViewModel.cs
│   ├── FacturaHistorialViewModel.cs
│   ├── FacturaDetalleViewModel.cs
│   └── FacturaEstadoViewModel.cs
│
└── wwwroot/
    └── js/
        └── facturacion-electronica.js
```

**Tareas:**
- [ ] Crear controller FacturacionElectronicaController
- [ ] Crear vista de configuración (RNC, datos empresa, certificado digital)
- [ ] Crear vista de historial de facturas
- [ ] Crear vista de detalle de factura
- [ ] Crear vista de estado de factura
- [ ] Crear vista de reintento de envío (contingencia)
- [ ] Crear viewmodels necesarios
- [ ] Añadir JavaScript para interacciones AJAX
- [ ] Integrar con controles de UI existentes del POS
- [ ] Añadir menú en navbar para acceso al módulo
- [ ] Añadir pruebas de UI (UI tests o E2E)

---

### Fase 7: Testing (2 semanas)

**Objetivo:** Validar que todo funciona correctamente antes de producción.

#### 7.1 Unit Tests

```
tests/
├── POS.Domain.Types.Tests/
│   ├── RNCTests.cs
│   ├── eNCFTests.cs
│   ├── FechaDominicanaTests.cs
│   ├── DecimalTypesTests.cs
│   ├── UnidadMedidaTests.cs
│   ├── ProvinciaMunicipioTests.cs
│   └── ImpuestoAdicionalTests.cs
│
├── POS.Application.Tests/
│   ├── EmitirFacturaCommandHandlerTests.cs
│   ├── AnularFacturaCommandHandlerTests.cs
│   ├── ConsultarEstadoFacturaCommandHandlerTests.cs
│   ├── InvoiceRequestGeneratorTests.cs
│   └── InvoiceValidationTests.cs
│
└── POS.Infrastructure.Tests/
    ├── XmlSerializerTests.cs
    │   ├── ECF32Tests.cs
    │   ├── ECF31Tests.cs
    │   ├── RFCE32Tests.cs
    │   ├── ANECFTests.cs
    │   ├── ACECFTests.cs
    │   └── ARECFTests.cs
    ├── XmlValidatorTests.cs
    ├── HashGeneratorTests.cs
    ├── DigitalSignatureTests.cs
    └── SqlInvoiceRepositoryTests.cs
```

**Tareas:**
- [ ] Unit tests para todos los ValueObjects
- [ ] Unit tests para XmlSerializer (todos los tipos)
- [ ] Unit tests para XmlValidator
- [ ] Unit tests para HashGenerator
- [ ] Unit tests para DigitalSignatureService
- [ ] Unit tests para Command Handlers
- [ ] Unit tests para DTOs y validaciones
- [ ] Coverage: objetivo >80% para Domain y Application, >70% para Infrastructure

#### 7.2 Integration Tests

```
tests/
└── POS.IntegrationTests/
    ├── XmlSerializationIntegrationTests.cs
    ├── ElectronicInvoiceFlowTests.cs
    │   ├── EmisionFacturaTests.cs
    │   ├── AnulacionFacturaTests.cs
    │   └── ConsultaEstadoTests.cs
    ├── DatabaseIntegrationTests.cs
    └── ContingencyFlowTests.cs
```

**Tareas:**
- [ ] Test de flujo completo: Sale → Generate XML → Validate → Submit
- [ ] Test de flujo de anulación
- [ ] Test de flujo de contingencia
- [ ] Test de persistencia en BD
- [ ] Test de reintento automático
- [ ] Test de errores y edge cases
- [ ] Test con DGII sandbox (si disponible)

#### 7.3 Pruebas E2E (si hay tiempo)

**Tareas:**
- [ ] Simulación de venta en POS
- [ ] Emisión automática de factura electrónica
- [ ] Impresión de factura
- [ ] Persistencia en BD
- [ ] Consulta de estado

---

### Fase 8: Documentación y Despliegue (1 semana)

**Objetivo:** Preparar el sistema para su despliegue en producción.

#### 8.1 Documentación

```
docs/
├── 01_Resumen_General.md       ← ya creado
├── 02_Mapeo_XSD_CSharp.md      ← ya creado
├── 03_Interfaces_Servicios.md  ← ya creado
├── 04_Diseno_Infraestructura_DGII.md  ← ya creado
├── 05_Plan_Implementacion.md   ← este documento
├── 06_Entidades_Dominio.md     ← pendiente
├── 07_Configuracion.md         ← pendiente
├── 08_Pruebas.md               ← pendiente
├── 09_Despliegue.md            ← pendiente
└── 10_Manual_Usuario.md        ← pendiente
```

**Tareas:**
- [ ] Completar documentación técnica
- [ ] Escribir manual de usuario para módulo de facturación electrónica
- [ ] Documentar procedimiento de configuración de empress
- [ ] Documentar procedimiento de configuración de certificado digital
- [ ] Documentar procedimiento de instalación
- [ ] Documentar troubleshooting común
- [ ] Documentar contactos de soporte

#### 8.2 CI/CD Pipeline

**Tareas:**
- [ ] Configurar GitHub Actions / Azure DevOps para build automático
- [ ] Configurar ejecución de pruebas en cada commit
- [ ] Configurar generación de artefactos (MSI installer)
- [ ] Configurar despliegue a entorno de testing
- [ ] Configurar despliegue a producción

#### 8.3 Instalador Windows

**Tareas:**
- [ ] Configurar proyecto instalador (WiX o InstallShield)
- [ ] Incluir todos los archivos necesarios
- [ ] Configurar instalación de base de datos (si necesario)
- [ ] Configurar instalación del servicio de contingencia
- [ ] Crear asistente de configuración inicial
- [ ] Probar instalación en máquina limpia

---

## 3. Cronograma Estimado

| Fase | Duración | Inicio | Fin |
|------|----------|--------|-----|
| Fase 1: Infraestructura básica | 2 semanas | Semana 1 | Semana 2 |
| Fase 2: Contratos de interfaces | 3 semanas | Semana 3 | Semana 5 |
| Fase 3: Implementación DGII | 4 semanas | Semana 6 | Semana 9 |
| Fase 4: Integración BD | 2 semanas | Semana 10 | Semana 11 |
| Fase 5: Casos de uso | 3 semanas | Semana 12 | Semana 14 |
| Fase 6: Integración UI | 2 semanas | Semana 15 | Semana 16 |
| Fase 7: Testing | 2 semanas | Semana 17 | Semana 18 |
| Fase 8: Documentación y despliegue | 1 semana | Semana 19 | Semana 19 |

**Total:** 19 semanas (~4.5 meses)

---

## 4. Priorización de Funcionalidades (MVP vs Full)

### 4.1 MVP (Versión Mínima Viable — 12 semanas)

**Funcionalidades esenciales:**
- [ ] Generación XML e-CF 32 (factura de consumo)
- [ ] Validación XML contra XSD
- [ ] Generación hash de seguridad
- [ ] Persistencia en BD
- [ ] Consulta de estado local
- [ ] Modo contingencia (guardar XML sin enviar)
- [ ] UI básica: configuración empresa + historial facturas
- [ ] Reintento manual de envío

**Sin:**
- [ ] Firma digital (usar sin firmar con nota legal aclaratoria)
- [ ] Envío automático a DGII (solo manual)
- [ ] Anulación de facturas (pendiente)
- [ ] Consulta estado en DGII (solo local)
- [ ] e-CF 31, 41, 47 (solo e-CF 32)
- [ ] Impuestos adicionales complejos
- [ ] Otra moneda
- [ ] Paginación
- [ ] Descuentos/recargos avanzados

### 4.2 Versión 1.0 Completa (19 semanas)

Todas las funcionalidades del MVP +:
- [ ] Envío automático a DGII mediante SOAP
- [ ] Firma digital con certificado A1
- [ ] ARECF y ACECF automáticos
- [ ] Anulación de facturas
- [ ] Consulta de estado en DGII
- [ ] e-CF 31 (factura de crédito fiscal)
- [ ] RFCE 32 (contingencia completa)
- [ ] Generación automática de eNCF
- [ ] Reintento automático de envío
- [ ] UI completa para gestión de facturas
- [ ] Logs y auditoría

### 4.3 Versiones Futuras (Post-1.0)

- [ ] e-CF 41 (compras), 43-47 (otros tipos)
- [ ] Integración con sistema bancario para conciliación
- [ ] Reportes y estadísticas de facturación
- [ ] Exportación a PDF/Excel
- [ ] Email automático de facturas al cliente
- [ ] Multi-empresa / multi-sucursal
- [ ] Facturación masiva
- [ ] API REST para integración externa
- [ ] Sistema de notificación (email, SMS) de estados

---

## 5. Gestión de Riesgos

| Riesgo | Impacto | Probabilidad | Mitigación |
|--------|---------|--------------|------------|
| Endpoint DGII cambia sin aviso | Alto | Media | Monitorear actualizaciones DGII, versionar esquemas, tener modo contingencia |
| Certificado digital complicado de obtener | Alto | Media | Planificar tiempo para obtenerlo, explorar opciones A1 vs A3, contactar proveedores |
| DGII tiene downtime frecuente | Medio | Alta | Implementar contingencia robusta, reintentos automáticos, alertas admin |
| Esquemas XSD no son exactamente como los documentos oficiales | Medio | Baja | Validar con ejemplos reales de DGII, contactar DGII para confirmar |
| Requisitos legales cambian | Alto | Baja | Mantener asesoría legal, monitor de cambios regulatorios |
| API SOAP DGII es diferente de lo esperado | Alto | Alta | Contactar DGII para obtener documentación SOAP oficial, probar con sandbox si existe |

---

## 6. Equipo Requerido

| Rol | Responsabilidades | Des dedication |
|-----|-------------------|---------------|
| Desarrollador Full-Stack (.NET) | Implementación completa del módulo | 100% (40h/semana) |
| QA/Tester | Pruebas manuales, automatización, validación con DGII | 50% (20h/semana) desde Fase 5 |
| PO/Producto | Definición de requisitos, priorización, validación | 25% (10h/semana) |
| Asesor Legal/Tributario (externo) | Validación de cumplimiento normativo, revisión de documentos | Consultas puntuales |
| Admin DGII (externo) | Obtención de certificado, configuración de accesos SOAP | Según necesidad |

---

## 7. Criterios de Éxito

### MVP (12 semanas)
- [ ] Sistema genera XML válido e-CF 32 que pasa validación XSD
- [ ] Sistema calcula correctamente ITBIS (18%, 16%, 0%, Exento)
- [ ] Sistema genera hash de seguridad correctamente
- [ ] Sistema persiste facturas en BD
- [ ] Sistema opera en modo contingencia cuando DGII no está disponible
- [ ] UI permite configurar empresa y ver historial de facturas
- [ ] Pruebas unitarias con cobertura >80% en Domain y Application

### Versión 1.0 (19 semanas)
- [ ] Todo lo del MVP + envío a DGII funciona
- [ ] Firma digital con certificado válido
- [ ] ARECF y ACECF son procesados automáticamente
- [ ] Anulación de facturas funciona
- [ ] Todos los tipos de comprobante principales funcionan (32, 31)
- [ ] Reintento automático de envío funciona
- [ ] UI completa para gestión integral
- [ ] Manuales de usuario y técnicos completos
- [ ] Pruebas de integración con DGII (cuando disponible)

---

## 8. Documentación Relacionada

- [01_Resumen_General.md](01_Resumen_General.md) — Visión general del sistema
- [02_Mapeo_XSD_CSharp.md](02_Mapeo_XSD_CSharp.md) — Mapeo de tipos XSD a C#
- [03_Interfaces_Servicios.md](03_Interfaces_Servicios.md) — Contratos de interfaces
- [04_Diseno_Infraestructura_DGII.md](04_Diseno_Infraestructura_DGII.md) — Diseño de infraestructura DGII
- [05_Plan_Implementacion.md](05_Plan_Implementacion.md) — Este documento

---

## 9. Siguientes Pasos Inmediatos

### Semana 1 (día 1-5)
- [ ] Aprobación del plan de implementación
- [ ] Crear estructura de carpetas del proyecto
- [ ] Configurar solución .NET 10 con proyectos
- [ ] Implementar RNC.cs, eNCF.cs, FechaDominicana.cs (Fase 1.1)
- [ ] Implementar TipoeCFType.cs, TipoIngresosType.cs, TipoPagoType.cs (Fase 1.1)
- [ ] Implementar UnidadMedidaType.cs, ProvinciaMunicipio.cs (Fase 1.1)
- [ ] Configurar tests para tipos

### Semana 1 (día 6-7)
- [ ] Implementar DecimalTypes.cs (todos los tipos decimales)
- [ ] Implementar Clases de tipos complejos básicos (Encabezado, Emisor, Comprador, Totales)
- [ ] Continuar implementación de tipos complejos (DetallesItems, Item, etc.)
- [ ] Añadir enumeraciones completas
- [ ] Añadir formateadores
- [ ] Primera revisión de código y pruebas

### Semana 2
- [ ] Completar todos los tipos (Fase 1 completada)
- [ ] Empezar Fase 2: definir interfaces
- [ ] Definir IElectronicInvoiceService, IElectronicInvoiceRepository, etc.
- [ ] Definir DTOs
- [ ] Definir excepciones

¿Deseas continuar con la fase 1 (tipos básicos) o prefieres revisar/modificar el plan antes de comenzar?
