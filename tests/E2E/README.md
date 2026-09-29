# Tests E2E de PosPalasy (Playwright)

Suite de extremo a extremo contra la app real corriendo en http://localhost:5099 (Development).

## Requisitos
- App levantada: `scripts/iniciar_pospalasy.ps1` (o `_wip_fase5/reiniciar_app.ps1`)
- Node 22+ (instalado) y Chromium de Playwright: `npx playwright install chromium`

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
- `workers: 1` y `retries: 1` en `playwright.config.ts`: la BD local (`PosPalasy_DGII`) es compartida y el test de cobro **muta datos reales** (crea una venta/e-CF nueva en cada corrida, avanza la secuencia).
- El cobro requiere turno de caja abierto: `helpers.ts > asegurarTurnoAbierto` lo abre si falta.
- Credenciales admin en `helpers.ts` (máquina local, no subir a producción).
- `trace: retain-on-failure` + screenshots automáticos en `test-results/`.
