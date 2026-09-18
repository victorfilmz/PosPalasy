# Módulo de Facturación Electrónica DGII — Documentación ❗
> Esta carpeta contiene la documentación técnica del sistema POS con módulo de facturación electrónica conforme a los esquemas XSD de la Dirección General de Impuestos (DGII) de la República Dominicana.
> ---
> 📁 [01_Resumen_General.md](01_Resumen_General.md) — Visión general del sistema y arquitectura
> 📁 [02_Mapeo_XSD_CSharp.md](02_Mapeo_XSD_CSharp.md) — Mapeo completo de tipos XSD a C#
> 📁 [03_Interfaces_Servicios.md](03_Interfaces_Servicios.md) — Contratos de interfaces y servicios
> 📁 [04_Diseno_Infraestructura_DGII.md](04_Diseno_Infraestructura_DGII.md) — Diseño de la infraestructura DGII
> 📁 [05_Plan_Implementacion.md](05_Plan_Implementacion.md) — Plan de implementación paso a paso
> 📁 [06_Seguridad_Matriz_Permisos.md](06_Seguridad_Matriz_Permisos.md) — **Seguridad: roles, matriz de permisos y controles aplicados**
> 📁 [07_Flujo_Venta_Transaccional.md](07_Flujo_Venta_Transaccional.md) — **Venta: caso de uso, transacción, estados, idempotencia, eNCF, inventario y caja**
> 📁 [08_Politica_Stock.md](08_Politica_Stock.md) — **Política de existencias (Permitir/Advertir/Bloquear) y auditoría del cambio**
> 📁 [09_Concurrencia_eNCF.md](09_Concurrencia_eNCF.md) — **Numeración fiscal bajo concurrencia: medición y solución**
> 📁 [README.md](README.md) — Documentación principal y guía de inicio
>
> ---
> ### XSDs Originales
> Los archivos XSD originales están en la carpeta `documentacion xsd/` (documentación xsd)
> - **e-CF 32 v.1.0.xsd**: Factura de Consumo Electrónica (tipo 32) — esquema más completo
> - **e-CF 31 v.1.0.xsd**: Factura de Crédito Fiscal Electrónica (tipo 31)
> - **e-CF 41 v.1.0.xsd**: Compras Electrónicas (tipo 41)
> - **e-CF 47 v.1.0.xsd**: Comprobante para Pagos al Exterior (tipo 47)
> - **e-CF 43..46 v.1.0.xsd**: Gastos menores, regímenes especiales, gubernamental, exportaciones
> - **RFCE 32 v.1.0.xsd**: Factura de consumo para contingencia (versión simplificada)
> - **ANECF v.1.0.xsd**: Anulación de NCF
> - **ACECF v.1.0.xsd**: Aprobación comercial
> - **ARECF v1.0.xsd**: Acuse de recibo
> - **Semilla v.1.0.xsd**: Contenedor base XML
>
> ---
> ### Arquitectura
> El sistema sigue Clean Architecture con .NET 10 / ASP.NET Core MVC:
>
> ```text
> POS/
> ├── src/
> │   ├── POS.Domain/              — Entidades, reglas de negocio
> │   ├── POS.Domain.Types/        — ValueObjects, tipos, enums (DGII)
> │   ├── POS.Application/         — Casos de uso, command handlers
> │   ├── POS.Application.Interfaces/ — Contratos (interfaces)
> │   ├── POS.Infrastructure/      — Implementación concreta
> │   │   ├── POS.Infrastructure.ElectronicInvoicing/ — Services DGII
> │   │   └── POS.Infrastructure.Persistence/ — EF Core, SQL
> │   └── POS.Web/                 — ASP.NET Core MVC, Razor Views
> ├── tests/
> │   ├── POS.Domain.Types.Tests/
> │   ├── POS.Application.Tests/
> │   └── POS.Infrastructure.Tests/
> └── docs/
>     └── documentacion xsd/       — XSDs originales de DGII
> ```
>
> ---
> ### Módulo de Facturación Electrónica
> El módulo permite emitir facturas electrónicas conforme a los esquemas XSD de la DGII:
> 1. **Emisión**: Generar XML e-CF 32, validar contra XSD, enviar a DGII, recibir ARECF + ACECF
> 2. **Anulación**: Generar XML ANECF para anular facturas anteriores
> 3. **Consulta**: Consultar estado de facturas en DGII
> 4. **Contingencia**: Guardar XML localmente cuando DGII no está disponible
>
> ---
> ### Estado del Proyecto
> | Fase | Estado | Duración estimada |
> |------|--------|-------------------|
> | Fase 1: Tipos básicos (Domain.Types) | 🟡 En progreso | 2 semanas |
> | Fase 2: Contratos de interfaces | ⬜ Por comenzar | 3 semanas |
> | Fase 3: Implementación DGII | ⬜ Por comenzar | 4 semanas |
> | Fase 4: Integración BD | ⬜ Por comenzar | 2 semanas |
> | Fase 5: Casos de uso | ⬜ Por comenzar | 3 semanas |
> | Fase 6: UI integración | ⬜ Por comenzar | 2 semanas |
> | Fase 7: Testing | ⬜ Por comenzar | 2 semanas |
> | Fase 8: Documentación y despliegue | ⬜ Por comenzar | 1 semana |
>
> **Total estimado:** 19 semanas (~4.5 meses) con equipo completo.
>
> ---
> ### Empezar a trabajar
> Para empezar a implementar, ejecutar:
> ```bash
> dotnet new sln -n POS
> dotnet new classlib -n POS.Domain.Types -o src/POS.Domain.Types
> dotnet new classlib -n POS.Application.Interfaces -o src/POS.Application.Interfaces
> dotnet new xunit -n POS.Domain.Types.Tests -o tests/POS.Domain.Types.Tests
> # ... (ver README.md para instrucciones completas)
> ```
>
> Luego implementar los ValueObjects en `POS.Domain.Types/` según la Fase 1 del plan.
>
> ---
> **Product Owner:** Víctor
> **Fecha:** 16/09/2026
