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
> 📁 [10_Caja_Devoluciones.md](10_Caja_Devoluciones.md) — **Caja: devoluciones transaccionales, reembolso, trazabilidad, arqueo y cierre**
> 📁 [11_Plan_Fase5_Fiscal.md](11_Plan_Fase5_Fiscal.md) — **Plan FASE 5 fiscal: XML-DSig, XSD, autenticación DGII, ANECF (gates G0–G5)**
> 📁 [12_HandOff_Homologacion_DGII.md](12_HandOff_Homologacion_DGII.md) — **Hand-off de homologación: checklist TestECF, certificado, plan de verificación y criterio de éxito**
> 📁 [13_Gate_Final_Fase5.md](13_Gate_Final_Fase5.md) — **Gate final de FASE 5: informe, evidencia y veredicto PASS**
> 📁 [14_Tramite_Certificado_A1.md](14_Tramite_Certificado_A1.md) — **Trámite operativo: certificado A1 y habilitación como emisor electrónico (testecf)**
> 📁 [15_Plan_Fase6.md](15_Plan_Fase6.md) — **Plan FASE 6: reporte 606, contingencia y portal de consultas (sub-fases 6.0–6.4)**
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
> | Fase | Estado | Referencia |
> |------|--------|------------|
> | Fase 1–4: Tipos, contratos, DGII base, BD y venta transaccional | ✅ Completadas (gates previos) | docs 05–10 |
> | Fase 5: Integración fiscal (XSD, firma, autenticación, RFCE/e-CF, NC 34, ANECF, secuencias) | ✅ **Completada — gate final PASS** | docs 11 y 13 |
> | Fase 6: reporte 606, régimen de contingencia y portal de consultas | ⬜ Plan listo — pendiente de autorización | doc 15 |
>
> **Nota:** el plan de 19 semanas de esta página es el estimado histórico del diseño; la ejecución real
> por fases y gates quedó registrada en los informes citados.
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
