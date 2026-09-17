# PosPalasy

Punto de venta (POS) con **facturación electrónica e-CF** conforme a la normativa de la DGII (República Dominicana).

## Arquitectura

Solución .NET 10 en capas (Clean Architecture):

| Proyecto | Rol |
|---|---|
| `src/POS.Domain` | Entidades, enums y contratos de repositorio del dominio fiscal |
| `src/POS.Domain.Types` | Tipos primitivos y tipos de datos compartidos |
| `src/POS.Application` | Casos de uso y servicios de aplicación |
| `src/POS.Infrastructure` | Persistencia (EF Core / SQL Server), cliente HTTP DGII, serialización XML, firma digital XML-DSig |
| `src/POS.UI` | App web ASP.NET Core (MVC) |
| `tests/POS.Domain.Types.Tests` | Pruebas unitarias |

## Características fiscales

- Emisión de e-CF 31 (Crédito Fiscal) y e-CF 32 (Consumo)
- Validación contra los **XSD oficiales de la DGII** (carpeta `documentacion xsd/`)
- Firma digital **XML-DSig** con certificado .pfx del emisor
- Cola de emisión resiliente (`EmisionesDGIIQueue`) para contingencia offline
- Reportes fiscales: formato **607**, resumen ITBIS (IT-1)
- Anulaciones e-CF (ANECF)

## Requisitos

- .NET SDK 10.0
- SQL Server / LocalDB

## Cómo ejecutar

```bash
dotnet run --project src/POS.UI
```

La aplicación inicializa la base de datos y datos semilla automáticamente.

> **Nota:** la configuración DGII incluye un modo simulador (`DGII:ModoSimulador=true` por defecto) para desarrollo sin conectarse al entorno real.

## Documentación

La documentación de diseño, arquitectura y mapeo XSD ↔ C# está en [`documentacion/`](documentacion/index.md). Los esquemas XSD oficiales de la DGII se usan en runtime para validar los comprobantes (ver `DgiiElectronicInvoiceService`).
