# Sistema POS — Facturación Electrónica DGII

**Repositorio:** POS Sistema  
**Módulo:** Facturación Electrónica  
**Estado:** En desarrollo  
**Última actualización:** 16/09/2026

---

## Índice de Documentación

### Documentación Técnica (docs/)

| Archivo | Descripción |
|---------|-------------|
| [01_Resumen_General.md](01_Resumen_General.md) | Visión general del sistema y arquitectura |
| [02_Mapeo_XSD_CSharp.md](02_Mapeo_XSD_CSharp.md) | Mapeo completo de tipos XSD a C# |
| [03_Interfaces_Servicios.md](03_Interfaces_Servicios.md) | Contratos de interfaces y servicios |
| [04_Diseno_Infraestructura_DGII.md](04_Diseno_Infraestructura_DGII.md) | Diseño de la infraestructura DGII |
| [05_Plan_Implementacion.md](05_Plan_Implementacion.md) | Plan de implementación paso a paso |

### XSDs Originales (documentacion xsd/)

Estos archivos son los esquemas XML originales proporcionados por la DGII:

| Archivo | Descripción |
|---------|-------------|
| `e-CF 32 v.1.0.xsd` | Factura de Consumo Electrónica (tipo 32) — **principal** |
| `e-CF 31 v.1.0.xsd` | Factura de Crédito Fiscal Electrónica (tipo 31) |
| `e-CF 41 v.1.0.xsd` | Compras Electrónicas (tipo 41) |
| `e-CF 47 v.1.0.xsd` | Comprobante para Pagos al Exterior (tipo 47) |
| `e-CF 43..46 v.1.0.xsd` | Gastos menores, regímenes especiales, gubernamental, exportaciones |
| `RFCE 32 v.1.0.xsd` | Factura de consumo para contingencia (versión simplificada) |
| `ANECF v.1.0.xsd` | Anulación de NCF |
| `ACECF v.1.0.xsd` | Aprobación comercial |
| `ARECF v1.0.xsd` | Acuse de recibo |
| `Semilla v.1.0.xsd` | Contenedor base XML |

---

## Arquitectura del Sistema

### Clean Architecture

```
POS/
├── src/
│   ├── POS.Domain/              — Entidades, reglas de negocio
│   ├── POS.Domain.Types/        — ValueObjects, tipos, enums (DGII)
│   ├── POS.Application/         — Casos de uso, command handlers
│   ├── POS.Application.Interfaces/ — Contratos (interfaces)
│   ├── POS.Infrastructure/      — Implementación concreta
│   │   ├── POS.Infrastructure.ElectronicInvoicing/ — DGII services
│   │   └── POS.Infrastructure.Persistence/ — EF Core, SQL
│   └── POS.Web/                 — ASP.NET Core MVC, Razor Views
├── tests/
│   ├── POS.Domain.Types.Tests/
│   ├── POS.Application.Tests/
│   └── POS.Infrastructure.Tests/
└── docs/
    └── documentacion xsd/       — XSDs originales de DGII
```

### Flujo de Dependencias

```
DOMAIN (ninguna dependencia externa)
  ↑
APPLICATION (depende de Domain, define interfaces)
  ↑
INFRASTRUCTURE (implementa interfaces, depende de DB/API)
  ↑
WEB (MVC → Application → Infrastructure → DB/API)
```

---

## Módulo de Facturación Electrónica

### Flujo Principal

```
Usuario cierra venta → CreateSaleHandler
    ↓
[Generar factura electrónica]
    ↓
ElectronicInvoiceService.SubmitAsync()
    ├── 1. Construir DTO desde Sale
    ├── 2. Validar reglas DGII
    ├── 3. Serializar XML (e-CF 32 XSD)
    ├── 4. Validar XML contra XSD
    ├── 5. Calcular Hash (CodigoSeguridadeCF)
    ├── 6. Firmar digitalmente (certificado DGII)
    ├── 7. Enviar a DGII (REST API JSON con TrackId)
    ├── 8. Recibir ARECF (acuse de recibo) y TrackId
    ├── 9. Polling para obtener ACECF (aprobación comercial)
    └── 10. Persistir resultado + XML en BD
    ↓
Venta finalizada, factura impresa, XML guardado
```

### Documentos DGII Adicionales

| Documento | Propósito |
|-----------|-----------|
| ANECF | Anulación de NCF — anula facturas anteriores |
| ACECF | Aprobación comercial — estado de aprobación |
| ARECF | Acuse de recibo — confirmación de recepción |

---

## Implementación — Estado Actual

| Componente | Estado | Notas |
|------------|--------|-------|
| POS.Domain.Types | Pendiente | Inicio Fase 1 |
| POS.Application.Interfaces | Pendiente | Inicio Fase 2 |
| XmlSerializer | Pendiente | Inicio Fase 3 |
| XmlValidator | Pendiente | Inicio Fase 3 |
| HashGenerator | Pendiente | Inicio Fase 3 |
| DigitalSignatureService | Pendiente | Pendiente certificado DGII |
| DgiiElectronicInvoiceService | Pendiente | Inicio Fase 3 |
| SqlInvoiceRepository | Pendiente | Inicio Fase 4 |
| Command Handlers | Pendiente | Inicio Fase 5 |
| UI Integración | Pendiente | Inicio Fase 6 |
| Testing | Pendiente | Inicio Fase 7 |

---

## Cómo Empezar a Trabajar

### Paso 1: Crear Proyectos

```bash
# Crear solución
dotnet new sln -n POS

# Crear proyectos
dotnet new classlib -n POS.Domain.Types -o src/POS.Domain.Types
dotnet new classlib -n POS.Application.Interfaces -o src/POS.Application.Interfaces
dotnet new classlib -n POS.Infrastructure.ElectronicInvoicing -o src/POS.Infrastructure.ElectronicInvoicing
dotnet new classlib -n POS.Infrastructure.Persistence -o src/POS.Infrastructure.Persistence
dotnet new webapp -n POS.Web -o src/POS.Web

# Proyectos tests
dotnet new xunit -n POS.Domain.Types.Tests -o tests/POS.Domain.Types.Tests
dotnet new xunit -n POS.Application.Tests -o tests/POS.Application.Tests
dotnet new xunit -n POS.Infrastructure.Tests -o tests/POS.Infrastructure.Tests

# Añadir referencias
dotnet sln add src/POS.Domain.Types/POS.Domain.Types.csproj
dotnet sln add src/POS.Application.Interfaces/POS.Application.Interfaces.csproj
dotnet sln add src/POS.Infrastructure.ElectronicInvoicing/POS.Infrastructure.ElectronicInvoicing.csproj
dotnet sln add src/POS.Infrastructure.Persistence/POS.Infrastructure.Persistence.csproj
dotnet sln add src/POS.Web/POS.Web.csproj
dotnet sln add tests/POS.Domain.Types.Tests/POS.Domain.Types.Tests.csproj
dotnet sln add tests/POS.Application.Tests/POS.Application.Tests.csproj
dotnet sln add tests/POS.Infrastructure.Tests/POS.Infrastructure.Tests.csproj

# Añadir referencias entre proyectos
dotnet add src/POS.Application.Interfaces reference src/POS.Domain.Types
dotnet add src/POS.Infrastructure.ElectronicInvoicing reference src/POS.Application.Interfaces
dotnet add src/POS.Infrastructure.Persistence reference src/POS.Application.Interfaces
dotnet add src/POS.Web reference src/POS.Infrastructure.Persistence
dotnet add tests/POS.Application.Tests reference src/POS.Application.Interfaces
dotnet add tests/POS.Infrastructure.Tests reference src/POS.Infrastructure.ElectronicInvoicing
```

### Paso 2: Empezar con Fase 1

Empezar implementando los ValueObjects en `POS.Domain.Types/`:

1. `RNC.cs` — ValueObject para RNC
2. `eNCF.cs` — ValueObject para eNCF
3. `FechaDominicana.cs` — ValueObject para fechas
4. Enums: `TipoeCFType`, `TipoIngresosType`, `TipoPagoType`, etc.
5. Tipos decimales: `DecimalMayorIgualCero`, `DecimalMayorCero`, etc.

Ver documento [05_Plan_Implementacion.md](05_Plan_Implementacion.md) para detalles completos de cada fase.

---

## Configuración

### appsettings.json (ejemplo)

```json
{
  "DGII": {
    "XsdPath": "documentacion xsd",
    "Endpoint": "",
    "Username": "",
    "Password": "",
    "CertificatePath": "",
    "CertificatePassword": "",
    "TimeoutSeconds": 30,
    "RetryCount": 3,
    "UseContingencyMode": false,
    "PollingIntervalSeconds": 60,
    "MaxPendingDays": 7
  },
  "Enterprise": {
    "RNC": "00000000000",
    "RazonSocial": "EMPRESA EMISORA S.A.",
    "NombreComercial": "",
    "Sucursal": "",
    "Direccion": "Calle Principal #123",
    "Municipio": "010100",
    "Provincia": "010000",
    "Telefono1": "809-555-1234",
    "Telefono2": "",
    "Correo": "facturacion@empresa.com",
    "WebSite": "",
    "ActividadEconomica": "COMERCIO AL POR MENOR",
    "CodigoVendedor": ""
  },
  "Tax": {
    "ITBIS1Rate": 0.18,
    "ITBIS2Rate": 0.16,
    "ITBIS3Rate": 0.00
  }
}
```

---

## Pruebas

### Ejecutar todas las pruebas

```bash
dotnet test
```

### Ejecutar pruebas de un proyecto específico

```bash
dotnet test tests/POS.Domain.Types.Tests/POS.Domain.Types.Tests.csproj
```

### Ejecutar con cobertura

```bash
dotnet test --collect:"XPlat Code Coverage"
```

---

## Contribución

1. Clonar repositorio
2. Crear branch de feature: `git checkout -b feature/nombre-feature`
3. Implementar cambios
4. Añadir pruebas
5. Ejecutar `dotnet test` para verificar
6. Hacer commit y push
7. Crear Pull Request

---

## Licencia

Propiedad intelectual de [Empresa]. Todos los derechos reservados.

---

## Contactos

- **Product Owner:** Víctor
- **Desarrollo:** Equipo de desarrollo POS
- **DGII:** www.dgii.gov.do
- **Soporte:** [email de soporte]

---

## Documentación Externa Relevante

- **Ley 172-12:** Ley de la Administración Tributaria Dominicana
- **DGII:** www.dgii.gov.do
- **Facturación Electrónica:** https://www.dgii.gov.do/serviciosonline/facturacion/
- **Certificados Digitales:** Proveedores autorizados por DGII

---

## Historial de Cambios

| Versión |Fecha | Cambios |
|---------|------|---------|
| 1.0 | 16/09/2026 | Documentación inicial del módulo |
