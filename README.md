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
# 1. Defina la contraseña de la cuenta inicial (nunca en appsettings.json)
dotnet user-secrets --project src/POS.UI set "Seguridad:AdminInicial:Password" "<contraseña-fuerte>"

# 2. Ejecute la aplicación
dotnet run --project src/POS.UI
```

La aplicación inicializa la base de datos, los datos semilla y la cuenta administradora inicial.
Si no define la contraseña inicial, el sistema genera una temporal, la registra una sola vez en el
log de arranque y **obliga a cambiarla en el primer acceso**.

> **Nota:** la configuración DGII incluye un modo simulador (`DGII:ModoSimulador=true` por defecto)
> para desarrollo sin conectarse al entorno real. Si el simulador está activo fuera del entorno
> `Development`, **la aplicación se niega a arrancar**: el simulador nunca puede facturar "de mentira"
> en producción.

## Seguridad

- Todas las pantallas y operaciones exigen sesión autenticada; el inicio de sesión es la única acción anónima.
- Roles: `SuperAdmin`, `Administrador`, `Supervisor`, `Cajero`, `Contador`, con políticas por operación
  (venta, anulación fiscal, ajustes de inventario, configuración, cambio de ambiente DGII, reportes).
- Toda petición que modifica estado exige token antiforgery (incluido el POST JSON del terminal POS).
- Contraseñas con PBKDF2 (sal por contraseña), política mínima, bloqueo tras 5 intentos fallidos.
- El certificado `.pfx` se guarda fuera del directorio de la aplicación y su contraseña se lee de
  user-secrets o variable de entorno (`Certificado:Password`), nunca del repositorio.

Detalle completo de roles, matriz de permisos y pruebas: [`documentacion/06_Seguridad_Matriz_Permisos.md`](documentacion/06_Seguridad_Matriz_Permisos.md).

## Pruebas

```bash
dotnet test PosPalasy.slnx
```

- `tests/POS.Domain.Types.Tests` — dominio, tipos DGII, cálculos y flujos del POS.
- `tests/POS.UI.SecurityTests` — seguridad de extremo a extremo sobre el pipeline HTTP real
  (autenticación, autorización por rol, antiforgery, cuentas y matriz de permisos).

## Documentación

La documentación de diseño, arquitectura y mapeo XSD ↔ C# está en [`documentacion/`](documentacion/index.md).

> **Aviso de fidelidad:** parte de la documentación de diseño describe la implementación **prevista**.
> Las funciones fiscales marcadas como pendientes (firma XML-DSig integrada, validación XSD en el flujo
> de emisión, ANECF conforme al esquema, autenticación DGII) **aún no están operativas**; consulte la
> auditoría técnica antes de considerar el sistema apto para producción.
