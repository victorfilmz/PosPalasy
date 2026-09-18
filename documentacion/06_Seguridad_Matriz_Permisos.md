# Seguridad — Matriz de permisos y controles implementados

> Este documento describe **lo que el sistema hace realmente** (verificado por pruebas automatizadas),
> no lo que se pretende implementar. Fase 0 del plan de corrección post-auditoría.

## 1. Modelo de roles

| Rol | Descripción | Ámbito |
|---|---|---|
| `SuperAdmin` | Administrador técnico del sistema | Todo, incluido cambio de ambiente DGII y gestión de usuarios |
| `Administrador` | Administrador del negocio | Empresa, certificado, impresión, catálogo y reportes |
| `Supervisor` | Supervisor de tienda | Anulación fiscal, reenvío, consultas DGII, ajustes de inventario, caja |
| `Cajero` | Operador de punto de venta | Venta, ticket, turno de caja, consulta de catálogo |
| `Contador` | Área contable | Reportes fiscales (607, IT-1) y consulta de comprobantes |

Los nombres canónicos viven en `POS.Domain.Enums.Roles` y los roles se otorgan como claim
`ClaimTypes.Role` en la cookie de sesión.

## 2. Matriz de permisos por operación

Policies definidas en `POS.UI.Security.Politicas`:

| Política | Roles | Operaciones protegidas |
|---|---|---|
| `OperacionPos` | SuperAdmin, Administrador, Supervisor, Cajero | `Pos` (terminal, ticket), `Caja` (turnos, cortes X/Z, movimientos), `Inventario` (consulta y kardex), `Facturacion/Lista` y `Facturacion/Detalle` |
| `Supervision` | SuperAdmin, Administrador, Supervisor | `Facturacion/Emitir`, `Facturacion/ConsultarEstado`, `Facturacion/Reenviar`, `Facturacion/Anular`, `Facturacion/Anulaciones`, `Inventario/AjustarStock` |
| `ReportesFiscales` | SuperAdmin, Administrador, Supervisor, Contador | `Reportes/Ventas607`, `Reportes/Exportar607Txt`, `Reportes/ResumenItbis` |
| `Configuracion` | SuperAdmin, Administrador | `Configuracion/Empresa`, `Configuracion/GuardarEmpresa`, `Configuracion/FacturaFisica`, `Configuracion/GuardarFacturaFisica`, `Configuracion/Certificado`, `Configuracion/CargarCertificado`, `Inventario/Crear`, `Inventario/Editar`, `Inventario/Eliminar` |
| `CambioAmbiente` | SuperAdmin | `Configuracion/CambiarAmbiente`, `Configuracion/ProbarConectividad` |
| `GestionUsuarios` | SuperAdmin | Reservada para la administración de cuentas (Fase 1) |
| (autenticado) | Todos | `Dashboard` |

**Regla de combinación:** los atributos de clase y de acción se acumulan (comportamiento estándar de MVC).
Por eso `Inventario/Crear` exige `OperacionPos` **y** `Configuracion`, y `CambiarAmbiente` exige
`Configuracion` **y** `CambioAmbiente` (es decir, solo SuperAdmin).

## 3. Controles aplicados en el pipeline

| Control | Implementación | Efecto |
|---|---|---|
| Autenticación por cookie | `AddAuthentication(...).AddCookie(...)` en `Program.cs` | Sin sesión válida no se accede a ninguna operación |
| Autorización por defecto | `AuthorizeFilter` global (`RequireAuthenticatedUser`) | Toda acción exige sesión salvo `[AllowAnonymous]` explícito (solo `Cuenta/*` y páginas informativas de `Home`) |
| Antiforgery | `AutoValidateAntiforgeryTokenAttribute` global | Toda petición que modifica estado sin token válido responde **400** (incluye el POST JSON del POS, que envía el token por cabecera `RequestVerificationToken`) |
| Cookies | `HttpOnly`, `SameSite=Strict`, `Secure` salvo desarrollo, 12 h deslizantes | Mitiga XSS/CSRF y robo de sesión vía tráfico no cifrado en producción |
| Cambio obligatorio | `CambioPasswordObligatorioMiddleware` | Una cuenta con contraseña temporal no puede operar hasta cambiarla |
| Bloqueo por intentos | `Usuario.RegistrarIntentoFallido` (5 intentos → 15 min) | Mitiga fuerza bruta |
| Anti-enumeración | Mensaje único + hash señuelo en `AutenticacionService` | No se puede distinguir usuario inexistente de contraseña incorrecta, ni por mensaje ni por tiempo |
| Hashing | `IdentityPasswordHasher` (PBKDF2-HMAC-SHA256 de ASP.NET Core Identity) | Contraseñas nunca en claro ni reversibles; migración automática si el framework pide rehash |
| Política de contraseñas | `PasswordPolicy` (≥12, mayúscula, minúscula, dígito, especial, sin espacios, no contiene el usuario) | Evita contraseñas débiles, incluidas las de cuentas iniciales |
| Certificado | Directorio de datos fuera del directorio de la aplicación; contraseña nunca persistida | El `.pfx` no se publica, no se versiona ni queda en el código |
| Guarda de entorno | `Program.cs` | Si `DGII:ModoSimulador=true` fuera de `Development`, el arranque falla con mensaje explícito: no se puede facturar simulando en producción |

## 4. Cuenta inicial y contraseñas

1. Si no existe ningún usuario, el sistema crea la cuenta indicada en `Seguridad:AdminInicial:Usuario`
   (por defecto `admin`) con rol `SuperAdmin`.
2. La contraseña se toma de `Seguridad:AdminInicial:Password`. **Nunca se versiona**: se define por
   `dotnet user-secrets` o variable de entorno (`Seguridad__AdminInicial__Password`).
3. Si no se definió o no cumple la política, se genera una contraseña temporal aleatoria, se registra
   **una sola vez** en el log de arranque y la cuenta queda marcada con cambio obligatorio.
4. La contraseña del certificado (`Certificado:Password`) sigue el mismo criterio: configuración
   externa, nunca en `appsettings.json` con valor real.

## 5. Pruebas automatizadas de seguridad (83)

`tests/POS.UI.SecurityTests` ejecuta el pipeline HTTP real (`WebApplicationFactory` + SQLite en memoria):

* **Autenticación:** 11 rutas protegidas redirigen al login sin sesión; login correcto emite cookie
  `HttpOnly`/`SameSite=Strict`; contraseña incorrecta no emite cookie; bloqueo tras 5 fallos.
* **Autorización:** cajero → 403 en configuración, anulación y cambio de ambiente; contador → 403 en POS
  y 200 en reportes; SuperAdmin → 200 en configuración.
* **Antiforgery:** 17 operaciones de estado responden 400 sin token y **no ejecutan** el efecto
  (se comprueba que el número de ventas no cambia); POST anónimo no ejecuta la venta.
* **Cuentas:** hashing con sal (dos hashes distintos de la misma contraseña), hash inválido → fallo sin
  excepción, política de contraseñas, bloqueo y liberación, cambio de contraseña propio.
* **Caso de uso de autenticación:** credenciales, anti-enumeración, bloqueo, cuenta inactiva,
  cambio de contraseña válido/ inválido / repetido.
* **Matriz de permisos (reflexión):** ninguna política pierde roles por accidente, todos los
  controladores declaran autorización salvo los públicos por diseño y las acciones sensibles conservan
  su política.

## 6. Pendientes conocidos (fases posteriores)

* Gestión de usuarios desde la interfaz (`GestionUsuarios` reservada; hoy no hay CRUD de usuarios).
* El cambio de ambiente DGII muta la configuración en memoria del proceso (Fase 7: persistencia y
  auditoría de cambios de configuración).
* `AllowedHosts: "*"` y ausencia de rate limiting / cabeceras de seguridad adicionales (Fase 14).
* El kardex y las ventas aún no registran el usuario que ejecutó la operación (Fase 13: auditoría).
* Sin segundo factor (MFA) para roles administrativos.
