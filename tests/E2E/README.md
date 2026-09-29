# Tests E2E de PosPalasy (Playwright)

Suite de extremo a extremo **totalmente aislada**: `global-setup.ts` recrea la base de datos
`PosPalasy_DGII_E2E` desde cero, levanta una instancia propia de la app en
http://localhost:5199 (con build en `tests/E2E/.app-build` para no chocar con la DLL de la
instancia de producción) y la detiene al terminar. La BD de producción (`PosPalasy_DGII`)
**nunca** se toca.

## Requisitos
- Node 22+ (instalado) y Chromium de Playwright: `npx playwright install chromium`
- La app NO necesita estar corriendo: el setup la levanta sola.

## Ejecutar
```bash
cd tests/E2E
npm test            # toda la suite (~30s)
npx playwright show-report   # reporte HTML
```

## Cobertura (16 tests)
| Área | Tests |
|---|---|
| Login | credenciales válidas → Dashboard; contraseña inválida → error; páginas protegidas redirigen a Login |
| Navegación | Dashboard, /Caja, /Facturacion/Lista, /Facturacion/Anulaciones, /Reportes/Ventas607, /Configuracion/Certificado — sin 4xx/5xx ni SqlException; /health responde |
| Certificado | estado "Válido y Activo" (CN Ensayo Homologacion) |
| Reportes | Ventas 607 carga con datos |
| Caja | estado del turno visible |
| Terminal POS | añadir producto (Refresco Cola 500ml) actualiza carrito con ITBIS 18%; **cobro completo → e-CF E320... generado y visible en /Facturacion/Lista** |

## Notas
- La BD E2E se recrea en cada corrida (DROP + CREATE + seed): los tests son idempotentes y la
  secuencia E32 siempre arranca en 1 (primer e-CF: `E320000000001`).
- El seed crea el usuario `admin` con `Seguridad:AdminInicial` (sin cambio obligatorio) y el
  catálogo de productos de `DbInitializer`.
- El cobro requiere turno de caja: `helpers.ts > asegurarTurnoAbierto` lo abre si falta y
  verifica duro que quedó abierto.
- `trace: retain-on-failure` + screenshots automáticos en `test-results/`.
