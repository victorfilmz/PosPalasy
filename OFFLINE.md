# PosPalasy — Funcionamiento sin internet (auditoría de red)

Fecha: 2026-09-30 · Aplica a la app en `http://localhost:5099` (rama `main`, desde `d8a2b94`).

## 1. Frontend: 100% offline ✅

Todo el frontend se sirve desde `wwwroot/` con `asp-append-version`:

| Recurso | Ruta local | Sustituye a |
|---|---|---|
| Bootstrap 5.3.3 CSS/JS | `/lib/bootstrap/dist/` | `cdn.jsdelivr.net` |
| Bootstrap Icons 1.11.3 | `/lib/bootstrap-icons/font/` | `cdn.jsdelivr.net` |
| Inter + Plus Jakarta Sans (latin) | `/fonts/*.woff2` + `/css/fonts.css` | `fonts.googleapis.com` |
| QR de verificación DGII | `/img/qr-dgii.png` (generado con `qrcode`) | `api.qrserver.com` |
| jQuery 3.7.1 | `/lib/jquery/dist/` | — (ya local) |

**Garantía continua**: la suite E2E (`cd tests/E2E && npx playwright test`) incluye una fixture automática que bloquea toda petición externa al servidor local en los 36 tests y hace fallar el test si la app intenta salir a internet. Si un futuro commit reintroduce un CDN, el CI lo detecta.

## 2. Backend: inventario de llamadas salientes

Todas las llamadas HTTP salientes del backend pasan por `HttpClient` registrados en `Program.cs` y apuntan a la DGII:

| # | Llamada | Código | Endpoint | ¿Requiere internet? |
|---|---|---|---|---|
| 1 | Autenticación (token DGII) | `DgiiAuthenticator.cs` (GetAsync/PostAsync) | `ecf.dgii.gov.do` / `fc.dgii.gov.do` según ambiente | Sí (en modo real) |
| 2 | Envío de e-CF / ANECF | `DgiiApiClient.EnviarFacturaAsync` / `EnviarAnulacionAsync` | `RecepcionECF` / `RecepcionRFCE` | Sí (en modo real) |
| 3 | Consulta de estado por TrackId | `DgiiApiClient.ConsultarEstadoAsync` | API de consulta DGII | Sí (en modo real) |
| 4 | Consulta RFCCE (por eNCF + código de seguridad) | `DgiiApiClient.ConsultarRFCEAsync` | `fc.dgii.gov.do` | Sí (en modo real) |
| 5 | Prueba de conectividad (botón en Configuración → Certificado) | `ConfiguracionController.ProbarConectividad` | `HostECF()` | Sí (es su propósito) |

No hay ninguna otra llamada saliente: ni telemetría, ni actualizaciones automáticas, ni CDN del lado del servidor.

**Estado actual**: `appsettings.json` tiene `DGII:ModoSimulador=true`. En este modo `DgiiApiClient` **no hace ninguna llamada de red**: genera TrackId y estados simulados localmente (`DgiiApiClient.cs` líneas 98/151/215/278/336). Además, `Program.cs` falla al arrancar si `ModoSimulador=true` fuera de Development — barrera anti-arranque accidental en producción real.

## 3. Qué funciona SIN internet

- **Todo el flujo de venta en el POS**: login, carrito, ITBIS 18%, cobro, generación de e-CF, XML firmado, impresión de ticket con QR local. La venta queda registrada en la BD (`PosPalasy_DGII`) con estado local (`XsdValidado`) aunque la transmisión a la DGII no se complete.
- **Caja**: apertura/cierre de turno, arqueo X/Z, devoluciones con nota de crédito, reportes (606/607, libros, dashboards).
- **Facturación**: anulación de e-CF/ANECF, colas y estados locales.
- **Cola de emisión DGII** (`DgiiQueueBackgroundService`, cada 15 s): procesa la cola local y reintenta; si no hay red, las excepciones de red se capturan y los comprobantes **quedan en cola pendientes** — se reintentan al recuperar conectividad.
- **Contingencia RFCE (offline)**: si la plataforma DGII no responde al cobrar, `ProcesarVentaHandler` declara automáticamente contingencia tipo 2 (FallaPlataformaDgii), el comprobante se emite en régimen de contingencia con ventana legal (hasta el fin del día hábil siguiente según `RegimenContingencia`), y el worker lo transmite cuando vuelve la red. También se puede declarar manualmente desde Facturación → Detalle.
- **Arranque de la app, health checks, backups locales y tareas programadas**.

## 4. Qué NO funciona sin internet (requiere conectividad)

- **Transmisión real a la DGII**: sin red los comprobantes no salen del estado local; quedan en cola/contingencia. Es el único comportamiento esperado y ya contemplado por el régimen de contingencia.
- **Consulta de estados reales** (TrackId / RFCE): sin red devuelven error de conexión y el estado local se mantiene.
- **Autenticación contra la DGII** al pasar a modo real (`ModoSimulador=false`): requiere red y certificado vigente.
- **Botón "Probar conectividad"**: sin red mostrará el fallo (es su función).
- Ojo: el QR impreso apunta a `ecf.dgii.gov.do/consultatimbre` — el **código** se genera local, pero **escanearlo** necesita internet en el dispositivo que escanea (es la URL pública de la DGII, no hay alternativa local).

## 5. Configuración por escenarios

| Escenario | `DGII:ModoSimulador` | `ASPNETCORE_ENVIRONMENT` | Red |
|---|---|---|---|
| Demo / operar sin DGII (actual) | `true` | `Development` | No necesaria |
| Homologación / pre-certificación | `false` | cualquiera | Necesaria (TestECF) |
| Producción DGII real | `false` | cualquiera | Necesaria (ecf) + certificado de producción (vence **2026-10-23**) |

Nota operativa: la app se reinicia desde `bin/Debug/net10.0/POS.UI.exe --urls http://localhost:5099`; si se relanza sin la variable `ASPNETCORE_ENVIRONMENT=Development` con el simulador activo, aborta por diseño (ver `Program.cs` línea 207).
