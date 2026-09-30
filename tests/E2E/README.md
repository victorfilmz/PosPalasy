# Tests E2E de PosPalasy (Playwright)

Suite de extremo a extremo **totalmente aislada**: `global-setup.ts` recrea la base de datos
`PosPalasy_DGII_E2E` desde cero, genera el QR DGII local si falta (`generar-qr.js`), levanta una
instancia propia de la app en http://localhost:5199 (con build en `tests/E2E/.app-build` para no
chocar con las DLL de la instancia de producción en :5099) y la detiene al terminar. La BD de
producción (`PosPalasy_DGII`) **nunca** se toca.

## Requisitos
- Node 22+ y Chromium de Playwright: `npx playwright install chromium`
- LocalDB corriendo (`sqllocaldb start MSSQLLocalDB`)
- La app NO necesita estar corriendo: el setup la levanta sola (timeout de arranque 300s;
  el build en frío puede tardar ~2 min).

## Ejecutar
```bash
cd tests/E2E
npm test            # toda la suite: 36 tests (~2 min)
npm test -- --retries=0   # sin reintentos (depuración dura)
npx playwright show-report   # reporte HTML
```

## Cobertura (36 tests)
| Área | Tests | Qué cubre |
|---|---|---|
| Login | 3 | credenciales válidas → Dashboard; contraseña inválida → error; páginas protegidas redirigen a Login |
| Navegación | 8 | Dashboard, /Caja, /Facturacion/Lista, /Facturacion/Anulaciones, /Reportes/Ventas607, /Configuracion/Certificado, etc. — sin 4xx/5xx ni SqlException; /health responde |
| Menú lateral | 15 | los 15 enlaces del sidebar: navegación a la URL correcta, carga sin errores de página y estado activo resaltado |
| Certificado / reportes / caja | 3 | estado "Válido y Activo" (CN Ensayo Homologacion); Ventas 607 con datos; estado del turno |
| Terminal POS | 2 | añadir producto (Refresco Cola 500ml) actualiza carrito con ITBIS 18%; **cobro completo → e-CF E320... generado y visible en /Facturacion/Lista** |
| Flujo fiscal | 5 | anulación de e-CF y ANECF; devolución con nota de crédito E34; cierre con arqueo Z; crear producto; código de producto duplicado rechazado |

## Garantía offline (fixture `bloquearExternos`)
`helpers.ts` registra una fixture automática (`{ auto: true }`) activa en **todos** los tests:
- Aborta cualquier petición que no vaya al servidor E2E local (localhost/127.0.0.1).
- Al terminar cada test, si la app hizo peticiones externas, el test **falla** listando las URLs.

Esto garantiza que la app se mantenga 100% offline de forma continua: si un commit reintroduce
un CDN (fonts, scripts, QR externos, etc.), el CI lo detecta de inmediato. Histórico de fugas
corregidas gracias a esta fixture: `cdn.jsdelivr.net` (Bootstrap), `fonts.googleapis.com`
(fuentes ahora en `wwwroot/fonts/`), `api.qrserver.com` (QR ahora en `wwwroot/img/qr-dgii.png`).

## QR DGII local
`generar-qr.js` (corre en el global setup, idempotente) genera `src/POS.UI/wwwroot/img/qr-dgii.png`
con el paquete npm `qrcode`, sustituyendo a `api.qrserver.com` en `Ticket.cshtml` y
`FacturaFisica.cshtml`. Se puede regenerar a mano con: `node generar-qr.js`.

## Notas
- La BD E2E se recrea en cada corrida (DROP + CREATE + seed): los tests son idempotentes y la
  secuencia E32 siempre arranca en 1 (primer e-CF: `E320000000001`, regex `/E32\d{10}/`).
- El seed crea el usuario `admin` / `PosPalasy#2026$Prod` vía `Seguridad__AdminInicial` (sin
  cambio obligatorio) y el catálogo de productos de `DbInitializer`.
  `Certificado__Password` = `ensayo-homologacion` (local) / `ci-test-password` (CI).
- El cobro requiere turno de caja: `helpers.ts > asegurarTurnoAbierto` lo abre si falta y
  verifica duro que quedó abierto.
- Al cambiar cualquier `.cshtml` o código hay que recompilar al build propio y reiniciar:
  el setup de la suite lo hace automáticamente (`dotnet run --property OutputPath=.app-build`),
  pero si la app debug de :5199 quedó corriendo de una sesión anterior, matarla primero
  (el setup necesita el puerto libre).
- `trace: retain-on-failure` + screenshots automáticos en `test-results/`.
- En CI (`.github/workflows/ci.yml`): windows-latest, LocalDB explícito, certificado de prueba
  con openssl, tests .NET + 36 E2E, artifacts de diagnóstico si falla.
