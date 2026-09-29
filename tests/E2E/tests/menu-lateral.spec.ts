import { expect } from '@playwright/test';
import { test as authedTest } from '../helpers';

// Rutas del menú lateral (texto visible del enlace → patrón de URL esperada).
const MENU: Array<[RegExp, RegExp]> = [
  [/Terminal POS/i, /\/Pos/],
  [/Turnos y Cuadre/i, /\/Caja$/],
  [/Devolución de Venta/i, /\/Caja\/Devolucion/],
  [/Catálogo de Productos/i, /\/Inventario$/],
  [/Nuevo Producto/i, /\/Inventario\/Crear/],
  [/Kardex de Movimientos/i, /\/Inventario\/Kardex/],
  [/Comprobantes e-CF/i, /\/Facturacion\/Lista/],
  [/Anulaciones \(ANECF\)/i, /\/Facturacion\/Anulaciones/],
  [/Dashboard Métricas/i, /:\d+\/$|\/Dashboard/],
  [/Libro Ventas 607/i, /\/Reportes\/Ventas607/],
  [/Libro Compras 606/i, /\/Reportes\/Compras606/],
  [/Resumen IT-1/i, /\/Reportes\/ResumenItbis/],
  [/Datos de la Empresa/i, /\/Configuracion\/Empresa/],
  [/Factura Física \/ Ticket/i, /\/Configuracion\/FacturaFisica/],
  [/Certificado Digital DGII/i, /\/Configuracion\/Certificado/],
];

authedTest.describe('Menú lateral', () => {
  for (const [etiqueta, urlEsperada] of MENU) {
    authedTest(
      `el enlace "${etiqueta.source}" del menú navega a su página`,
      async ({ authedPage: page }) => {
        await page.goto('/');
        // El enlace vive en el <aside>/nav del layout; el click debe navegar.
        const enlace = page.locator('aside a, nav a').filter({ hasText: etiqueta }).first();
        await expect(enlace, `el enlace ${etiqueta.source} existe en el menú`).toBeVisible();
        await enlace.click();
        await page.waitForLoadState('networkidle');

        expect(page.url(), `URL tras pulsar ${etiqueta.source}`).toMatch(urlEsperada);
        // La página de destino carga de verdad (sin error de servidor).
        await expect(page.locator('body')).not.toContainText(/SqlException|Internal Server Error/i);
        // Y el enlace queda marcado como activo (el resaltado del layout).
        await expect
          .poll(async () => {
            const activo = await page
              .locator('a.sidebar-link.active')
              .allInnerTexts();
            return activo.some(t => etiqueta.test(t));
          }, { timeout: 5_000 })
          .toBe(true);
      },
    );
  }
});
